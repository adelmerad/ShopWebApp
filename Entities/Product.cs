namespace ShopWebApp.Entities;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }

    public int categoryId { get; set; }         // cle etrangere
    public Category Category { get; set; } = null!;    // navigation
}
