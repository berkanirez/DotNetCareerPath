namespace StockPilot.Api.Models;

public class Product
{
    public int Id { get; set; }
    public string Sku { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }

    public Product(string sku, string name, decimal price)
    {
        Sku = sku;
        Name = name;
        Price = price;
    }
}
