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

    // AsNoTracking(): this data is only ever displayed, never modified through
    // this call — no reason to pay for EF Core's change tracker recording a
    // snapshot of every row just in case something gets edited later.
    public async Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Products.AsNoTracking().ToListAsync(cancellationToken);
    }

    // Deliberately NOT AsNoTracking(): RemoveAsync below calls this method to
    // find the entity it then deletes, so it needs a tracked instance —
    // UpdateAsync's own FindAsync call (below) needs the same for the same reason.
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

    public async Task<IReadOnlyList<Product>> AddRangeAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken = default)
    {
        // Each product goes through the same one-at-a-time AddAsync used by
        // the single-product Create endpoint, so a duplicate SKU fails the
        // exact same way (a real DbUpdateException from the unique index).
        // The difference is what happens AFTER a failure: AddAsync alone
        // would leave whatever succeeded before the failure permanently
        // committed. Wrapping the whole loop in one explicit transaction
        // means that if ANY product fails, nothing this call already
        // committed survives — CommitAsync is only reached if every product
        // succeeds; otherwise the transaction is disposed without being
        // committed, which rolls it back automatically.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        foreach (var product in products)
        {
            await AddAsync(product, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return products;
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
