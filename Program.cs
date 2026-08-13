using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

const string AUTH = "http://localhost:5124";   // serveur d'auth (OpenIddict)
const string SHOP = "http://localhost:5050";   // API ressource (ShopApi)

// --- Authentification : cookie pour le navigateur, OIDC pour parler au serveur d'auth ---
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name = "WebApp.Session";
    options.Cookie.HttpOnly = true;            // inaccessible au JS (anti-XSS)
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SessionStore = new MemoryTicketStore();  // tokens stockés côté serveur
    options.ExpireTimeSpan = TimeSpan.FromMinutes(10); // session courte (10 min)
    options.SlidingExpiration = false;                 // expiration FERME (pas prolongée)
})
.AddOpenIdConnect(options =>
{
    options.Authority = AUTH;                   // découverte OIDC + JWKS récupérés ici
    options.ClientId = "postman";              // client public + PKCE
    options.RequireHttpsMetadata = false;      // dev : authority en http
    options.ResponseType = "code";             // Authorization Code
    options.ResponseMode = "query";            // code renvoyé en ?code= (au lieu de form_post)
    options.Prompt = "login";                  // force la page de login à chaque connexion
    options.UsePkce = true;                     // PKCE
    options.SaveTokens = true;                   // stocke les tokens CÔTÉ SERVEUR (dans le cookie)
    options.GetClaimsFromUserInfoEndpoint = false;

    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("email");
    options.Scope.Add("profile");
    options.Scope.Add("offline_access");
    options.Scope.Add("shop_api");             // -> access token avec aud=shop_api

    options.MapInboundClaims = false;
    options.TokenValidationParameters.NameClaimType = "name";
    options.TokenValidationParameters.RoleClaimType = "role";

    // Cookies de corrélation/nonce en Lax : indispensable en HTTP (dev)
    options.NonceCookie.SameSite = SameSiteMode.Lax;
    options.CorrelationCookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddAuthorization();
builder.Services.AddHttpClient();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Page front servie par le BFF (même origine -> pas de CORS, pas de token dans le navigateur)
app.MapGet("/", () => Results.Content(Front.Html, "text/html"));

// ---------- Auth ----------
app.MapGet("/auth/login", () =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = "/" },
        new[] { OpenIdConnectDefaults.AuthenticationScheme }));

app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { ok = true });
});

app.MapGet("/auth/me", (ClaimsPrincipal user) =>
    user.Identity?.IsAuthenticated == true
        ? Results.Ok(new
        {
            name = user.FindFirst("name")?.Value ?? user.FindFirst("email")?.Value,
            email = user.FindFirst("email")?.Value,
            sub = user.FindFirst("sub")?.Value
        })
        : Results.Unauthorized());  // 401 propre (pas de redirection) pour le fetch JS

// ---------- Proxy vers ShopApi : le BFF ajoute le Bearer (token jamais exposé au navigateur) ----------
async Task<IResult> ProxyAsync(HttpContext ctx, IHttpClientFactory factory, HttpMethod method, string path, bool requireAuth)
{
    if (requireAuth && ctx.User.Identity?.IsAuthenticated != true)
        return Results.Unauthorized();

    var request = new HttpRequestMessage(method, SHOP + path);

    // On récupère le token stocké côté serveur et on l'injecte.
    var token = await ctx.GetTokenAsync("access_token");
    if (!string.IsNullOrEmpty(token))
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

    if (method == HttpMethod.Post)
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync();
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
    }

    var response = await factory.CreateClient().SendAsync(request);
    var content = await response.Content.ReadAsStringAsync();
    return Results.Text(content, "application/json", Encoding.UTF8, (int)response.StatusCode);
}

app.MapGet("/api/products", (HttpContext c, IHttpClientFactory f) => ProxyAsync(c, f, HttpMethod.Get, "/api/products", requireAuth: false));
app.MapGet("/api/categories", (HttpContext c, IHttpClientFactory f) => ProxyAsync(c, f, HttpMethod.Get, "/api/categories", requireAuth: false));
app.MapPost("/api/products", (HttpContext c, IHttpClientFactory f) => ProxyAsync(c, f, HttpMethod.Post, "/api/products", requireAuth: true));
app.MapPost("/api/categories", (HttpContext c, IHttpClientFactory f) => ProxyAsync(c, f, HttpMethod.Post, "/api/categories", requireAuth: true));

app.Run();
