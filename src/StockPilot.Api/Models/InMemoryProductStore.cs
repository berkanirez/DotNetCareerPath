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

    // No real I/O happens here — Task.FromResult(...) satisfies IProductStore's
    // async signature (kept consistent with EfProductStore) without pretending
    // there's actual asynchronous work to do.
    public Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<Product>>(_products);
    }

    public Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        foreach (var product in _products)
        {
            if (product.Id == id)
            {
                return Task.FromResult<Product?>(product);
            }
        }

        return Task.FromResult<Product?>(null);
    }

    public Task<Product> AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        product.Id = _nextId++;
        _products.Add(product);
        return Task.FromResult(product);
    }

    public async Task<bool> RemoveAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await GetByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return false;
        }

        _products.Remove(product);
        return true;
    }
}
