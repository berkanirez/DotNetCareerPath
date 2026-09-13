using StockPilot.Api.Models;

namespace StockPilot.Api.Data;

public static class DbSeeder
{
    public static void Seed(StockPilotDbContext context)
    {
        if (context.Products.Any())
        {
            return;
        }

        context.Products.AddRange(
            new Product("SKU-001", "Wireless Mouse", 19.99m),
            new Product("SKU-002", "Mechanical Keyboard", 79.99m),
            new Product("SKU-003", "USB-C Hub", 34.50m)
        );

        context.SaveChanges();
    }
}
