namespace StockPilot.Api.Models;

public interface IProductStore
{
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken = default);
    Task<Product> AddAsync(Product product, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Product>> AddRangeAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken = default);
    Task<Product?> UpdateAsync(int id, string name, decimal price, byte[] rowVersion, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(int id, CancellationToken cancellationToken = default);
}
