namespace StockPilot.Api.Models;

public record ProductDto(int Id, string Sku, string Name, decimal Price, byte[] RowVersion);
