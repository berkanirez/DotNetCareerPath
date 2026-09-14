using Microsoft.EntityFrameworkCore;
using StockPilot.Api.Models;

namespace StockPilot.Api.Data;

public class EfProductStore : IProductStore
{
    private readonly StockPilotDbContext _context;

    public EfProductStore(StockPilotDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Products.ToListAsync(cancellationToken);
    }

    public async Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Products.FindAsync([id], cancellationToken);
    }

    public async Task<bool> SkuExistsAsync(string sku, CancellationToken cancellationToken = default)
    {
        return await _context.Products.AnyAsync(p => p.Sku == sku, cancellationToken);
    }

    public async Task<Product> AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        _context.Products.Add(product);
        await _context.SaveChangesAsync(cancellationToken);
        return product;
    }

    public async Task<Product?> UpdateAsync(int id, string name, decimal price, byte[] rowVersion, CancellationToken cancellationToken = default)
    {
        var product = await _context.Products.FindAsync([id], cancellationToken);
        if (product is null)
        {
            return null;
        }

        // Tell EF Core "this is the version I actually read" rather than trusting
        // whatever FindAsync just returned — the WHERE clause SaveChangesAsync
        // generates compares THIS value against the database's current row,
        // not the value already sitting on the tracked entity. If another
        // request updated the row since our client last read it, no rows match
        // and SaveChangesAsync throws DbUpdateConcurrencyException.
        _context.Entry(product).Property(p => p.RowVersion).OriginalValue = rowVersion;

        product.Name = name;
        product.Price = price;

        await _context.SaveChangesAsync(cancellationToken);
        return product;
    }

    public async Task<bool> RemoveAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await GetByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return false;
        }

        _context.Products.Remove(product);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
