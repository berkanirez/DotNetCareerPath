namespace StockPilot.Api.Models;

public class InMemoryProductStore : IProductStore
{
    private readonly List<Product> _products = new()
    {
        new Product("SKU-001", "Wireless Mouse", 19.99m) { Id = 1 },
        new Product("SKU-002", "Mechanical Keyboard", 79.99m) { Id = 2 },
        new Product("SKU-003", "USB-C Hub", 34.50m) { Id = 3 }
    };

    private int _nextId = 4;

    public IReadOnlyList<Product> GetAll()
    {
        return _products;
    }

    public Product? GetById(int id)
    {
        foreach (var product in _products)
        {
            if (product.Id == id)
            {
                return product;
            }
        }

        return null;
    }

    public Product Add(Product product)
    {
        product.Id = _nextId++;
        _products.Add(product);
        return product;
    }

    public bool Remove(int id)
    {
        var product = GetById(id);
        if (product is null)
        {
            return false;
        }

        _products.Remove(product);
        return true;
    }
}
