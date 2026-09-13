namespace StockPilot.Api.Models;

public record CreateProductRequest(string Sku, string Name, decimal Price);
