using System.Net.Http.Headers;
using System.Text;
using ShopWebApp.Endpoints;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

const string SHOP = "http://localhost:5050";   // API ressource (ShopApi, même machine)

// --- Authentification : cookie simple, échange OIDC fait à la main (AuthEndpoints.cs) ---
// Même méthode que ClientApi (le BFF du binôme) : PKCE manuel, pas de middleware
// AddOpenIdConnect. Voir Endpoints/AuthEndpoints.cs pour le flow complet.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ShopWebApp.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);

        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };

        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpClient();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Page front servie par le BFF (même origine -> pas de CORS, pas de token dans le navigateur)
app.MapGet("/", () => Results.Content(Front.Html, "text/html"));

app.MapAuthEndpoints();

// ---------- Proxy vers ShopApi : le BFF ajoute le Bearer (token jamais exposé au navigateur) ----------
async Task<IResult> ProxyAsync(HttpContext ctx, IHttpClientFactory factory, HttpMethod method, string path, bool requireAuth)
{
    if (requireAuth && ctx.User.Identity?.IsAuthenticated != true)
        return Results.Unauthorized();

    var request = new HttpRequestMessage(method, SHOP + path);

    // Le token est stocké comme claim dans le cookie (cf. AuthEndpoints.cs), pas
    // via un ticket store séparé — même méthode que ClientApi.
    var token = ctx.User.FindFirst("access_token")?.Value;
    if (!string.IsNullOrEmpty(token))
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

    if (method == HttpMethod.Post || method == HttpMethod.Put)
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
app.MapDelete("/api/products/{id}", (int id, HttpContext c, IHttpClientFactory f) => ProxyAsync(c, f, HttpMethod.Delete, $"/api/products/{id}", requireAuth: true));
app.MapPut("/api/products/{id}", (int id, HttpContext c, IHttpClientFactory f) => ProxyAsync(c, f, HttpMethod.Put, $"/api/products/{id}", requireAuth: true));

app.Run();
