namespace ShopWebApp.Entities;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // navigation : une categorie contient plusieurs produits
    public List<Product> Products { get; set; } = new();
}
