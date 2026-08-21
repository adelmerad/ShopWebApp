using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using ShopWebApp.Data;
using ShopWebApp.Endpoints;
using ShopWebApp.Entities;

var builder = WebApplication.CreateBuilder(args);

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

// Produits/catégories fusionnés directement dans ShopWebApp (ShopApi retiré) :
// même application pour tout, comme ClientApi chez le binôme.
builder.Services.AddDbContext<ShopDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Page front servie par le BFF (même origine -> pas de CORS, pas de token dans le navigateur)
app.MapGet("/", () => Results.Content(Front.Html, "text/html"));

app.MapAuthEndpoints();

// ---------- Produits / catégories : accès direct à la base, plus de proxy vers une API séparée ----------
app.MapGet("/api/categories", async (ShopDbContext db) =>
    await db.Categories.ToListAsync());

app.MapPost("/api/categories", async (Category category, ShopDbContext db) =>
{
    db.Categories.Add(category);
    await db.SaveChangesAsync();
    return Results.Created($"/api/categories/{category.Id}", category);
}).RequireAuthorization();

app.MapGet("/api/products", async (ShopDbContext db) =>
    await db.Products.Include(p => p.Category).ToListAsync());

app.MapGet("/api/products/{id}", async (int id, ShopDbContext db) =>
{
    var product = await db.Products.Include(p => p.Category).FirstOrDefaultAsync(p => p.Id == id);
    return product is not null ? Results.Ok(product) : Results.NotFound();
});

app.MapPost("/api/products", async (Product product, ShopDbContext db) =>
{
    db.Products.Add(product);
    await db.SaveChangesAsync();
    return Results.Created($"/api/products/{product.Id}", product);
}).RequireAuthorization();

app.MapPut("/api/products/{id}", async (int id, Product updated, ShopDbContext db) =>
{
    var product = await db.Products.FindAsync(id);
    if (product is null)
        return Results.NotFound();

    product.Name = updated.Name;
    product.Price = updated.Price;
    product.categoryId = updated.categoryId;
    await db.SaveChangesAsync();
    return Results.Ok(product);
}).RequireAuthorization();

app.MapDelete("/api/products/{id}", async (int id, ShopDbContext db) =>
{
    var product = await db.Products.FindAsync(id);
    if (product is null)
        return Results.NotFound();

    db.Products.Remove(product);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.Run();
