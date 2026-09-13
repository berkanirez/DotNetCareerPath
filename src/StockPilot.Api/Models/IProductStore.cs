namespace StockPilot.Api.Models;

public interface IProductStore
{
    IReadOnlyList<Product> GetAll();
    Product? GetById(int id);
    Product Add(Product product);
    bool Remove(int id);
}
