using System.Text.Json.Serialization;

namespace ShopWebApp.Entities;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // navigation : une categorie contient plusieurs produits
    // [JsonIgnore] casse le cycle Category -> Products -> Category -> ... lors de la serialisation
    [JsonIgnore]
    public List<Product> Products { get; set; } = new();
}
