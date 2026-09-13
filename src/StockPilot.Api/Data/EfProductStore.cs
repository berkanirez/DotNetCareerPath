using StockPilot.Api.Models;

namespace StockPilot.Api.Data;

public class EfProductStore : IProductStore
{
    private readonly StockPilotDbContext _context;

    public EfProductStore(StockPilotDbContext context)
    {
        _context = context;
    }

    public IReadOnlyList<Product> GetAll()
    {
        return _context.Products.ToList();
    }

    public Product? GetById(int id)
    {
        return _context.Products.Find(id);
    }

    public Product Add(Product product)
    {
        _context.Products.Add(product);
        _context.SaveChanges();
        return product;
    }

    public bool Remove(int id)
    {
        var product = GetById(id);
        if (product is null)
        {
            return false;
        }

        _context.Products.Remove(product);
        _context.SaveChanges();
        return true;
    }
}
