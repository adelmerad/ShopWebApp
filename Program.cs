using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using ShopWebApp.Data;
using ShopWebApp.Endpoints;
using ShopWebApp.Entities;

var builder = WebApplication.CreateBuilder(args);

// --- Authentification 

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

static bool HasRole(ClaimsPrincipal user, params string[] roles) =>
    user.Claims.Any(c => c.Type == ClaimTypes.Role &&
        roles.Any(r => string.Equals(c.Value, r, StringComparison.OrdinalIgnoreCase)));

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("admin", policy => policy.RequireAssertion(ctx => HasRole(ctx.User, "admin")));
    options.AddPolicy("admin-ou-employe", policy => policy.RequireAssertion(ctx => HasRole(ctx.User, "admin", "employe")));
});
builder.Services.AddHttpClient();

builder.Services.AddDbContext<ShopDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Content(Front.Html, "text/html"));

app.MapAuthEndpoints();

// accès direct à la base
app.MapGet("/api/categories", async (ShopDbContext db) =>
    await db.Categories.ToListAsync());

app.MapPost("/api/categories", async (Category category, ShopDbContext db) =>
{
    db.Categories.Add(category);
    await db.SaveChangesAsync();
    return Results.Created($"/api/categories/{category.Id}", category);
}).RequireAuthorization("admin");

app.MapDelete("/api/categories/{id}", async (int id, ShopDbContext db) =>
{
    var category = await db.Categories.FindAsync(id);
    if (category is null)
        return Results.NotFound();

    db.Categories.Remove(category);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization("admin");

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
}).RequireAuthorization("admin-ou-employe");

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
}).RequireAuthorization("admin-ou-employe");

app.MapDelete("/api/products/{id}", async (int id, ShopDbContext db) =>
{
    var product = await db.Products.FindAsync(id);
    if (product is null)
        return Results.NotFound();

    db.Products.Remove(product);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization("admin");

app.Run();
