namespace StockPilot.Api.Models;

public class Product
{
    public int Id { get; set; }
    public string Sku { get; set; }
    public string Name { get; set; }
    public decimal Price { get; set; }

    // Populated by SQL Server itself (a real `rowversion` column) on every
    // insert/update — this is the optimistic concurrency token, not
    // application data. EfProductStore relies on it to detect lost updates.
    public byte[] RowVersion { get; set; } = null!;

    public Product(string sku, string name, decimal price)
    {
        Sku = sku;
        Name = name;
        Price = price;
    }
}
