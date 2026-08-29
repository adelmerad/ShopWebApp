using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ShopWebApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ShopWebApp.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/auth");

        // ===== 1. Démarrer la connexion =====

        group.MapGet("/login", (HttpContext context, IConfiguration config) =>
        {
            var verifier = PkceService.GenerateCodeVerifier();
            var challenge = PkceService.GenerateCodeChallenge(verifier);
            var state = PkceService.GenerateState();

            context.Response.Cookies.Append("pkce_verifier", verifier, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(10)
            });

            context.Response.Cookies.Append("oauth_state", state, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(10)
            });

            var url = $"{config["Sso:Authority"]}/connect/authorize" +
                      $"?client_id={Uri.EscapeDataString(config["Sso:ClientId"]!)}" +
                      $"&redirect_uri={Uri.EscapeDataString(config["Sso:RedirectUri"]!)}" +
                      $"&response_type=code" +
                      $"&scope={Uri.EscapeDataString(config["Sso:Scope"] ?? "openid profile email")}" +
                      $"&state={state}" +
                      $"&code_challenge={challenge}" +
                      $"&code_challenge_method=S256" +
                      $"&prompt=login";

            return Results.Redirect(url);
        });

        // ===== 2. Recevoir le code et l'échanger =====

        group.MapGet("/callback", async (
            HttpContext context,
            IConfiguration config,
            IHttpClientFactory httpClientFactory,
            IConfigurationManager<OpenIdConnectConfiguration> oidcConfig,
            TokenStore tokenStore,
            string? code,
            string? state) =>
        {
            if (string.IsNullOrEmpty(code))
                return Results.BadRequest("Code manquant.");

            var savedState = context.Request.Cookies["oauth_state"];
            if (string.IsNullOrEmpty(savedState) || savedState != state)
                return Results.BadRequest("State invalide.");

            var verifier = context.Request.Cookies["pkce_verifier"];
            if (string.IsNullOrEmpty(verifier))
                return Results.BadRequest("Verifier introuvable.");

            var client = httpClientFactory.CreateClient();

            var response = await client.PostAsync(
                $"{config["Sso:Authority"]}/connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = config["Sso:RedirectUri"]!,
                    ["client_id"] = config["Sso:ClientId"]!,
                    ["client_secret"] = config["Sso:ClientSecret"]!,
                    ["code_verifier"] = verifier
                }));

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return Results.BadRequest($"Échec de l'échange : {error}");
            }

            var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
            if (tokens is null)
                return Results.BadRequest("Réponse invalide.");

            // Validation reelle de l'id_token : signature (via les cles JWKS du SSO,
            // recuperees et mises en cache automatiquement), issuer, audience et
            // expiration. Avant, on lisait juste le contenu sans rien verifier.
            var discovery = await oidcConfig.GetConfigurationAsync(context.RequestAborted);
            var validationParameters = new TokenValidationParameters
            {
                ValidIssuer = discovery.Issuer,
                ValidAudience = config["Sso:ClientId"],
                IssuerSigningKeys = discovery.SigningKeys,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true
            };

            ClaimsPrincipal validated;
            try
            {
                var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
                validated = handler.ValidateToken(tokens.IdToken, validationParameters, out _);
            }
            catch (SecurityTokenException ex)
            {
                return Results.BadRequest($"id_token invalide : {ex.Message}");
            }

            // Les vrais tokens restent cote serveur ; le cookie ne porte qu'un
            // identifiant opaque (session_id) qui sert de cle pour les retrouver.
            var sessionId = TokenStore.NewSessionId();
            tokenStore.Set(sessionId,
                new StoredTokens(tokens.AccessToken, tokens.RefreshToken, DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn)),
                TimeSpan.FromHours(8));

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, validated.FindFirstValue("sub") ?? ""),
                new(ClaimTypes.Email, validated.FindFirstValue("email") ?? ""),
                new(ClaimTypes.Name, validated.FindFirstValue("name") ?? ""),
                new("session_id", sessionId)
            };

            // Les roles emis par ShopAuth (claim "role" dans l'id_token) deviennent des
            // ClaimTypes.Role ici, pour que .RequireAuthorization(policy => policy.RequireRole(...)) marche.
            foreach (var role in validated.FindAll("role"))
                claims.Add(new Claim(ClaimTypes.Role, role.Value));

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            context.Response.Cookies.Delete("pkce_verifier");
            context.Response.Cookies.Delete("oauth_state");

            return Results.Redirect("/");
        });

        // ===== 3. Qui suis-je ? =====
        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            if (user.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            return Results.Ok(new
            {
                id = user.FindFirstValue(ClaimTypes.NameIdentifier),
                email = user.FindFirstValue(ClaimTypes.Email),
                name = user.FindFirstValue(ClaimTypes.Name),
                roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value)
            });
        });

        group.MapPost("/logout", async (HttpContext context, TokenStore tokenStore) =>
        {
            var sessionId = context.User.FindFirstValue("session_id");
            if (sessionId is not null)
                tokenStore.Remove(sessionId);

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { message = "Déconnecté" });
        });
    }
}
