using Microsoft.EntityFrameworkCore;
using ShopWebApp.Entities;

namespace ShopWebApp.Data;

public class ShopDbContext : DbContext
{
    public ShopDbContext(DbContextOptions<ShopDbContext> options) : base(options) { }

    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Product>()
            .Property(p => p.Price)
            .HasPrecision(18, 2);

        builder.Entity<Product>()
            .HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.categoryId);
    }
}
