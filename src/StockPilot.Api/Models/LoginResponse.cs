namespace StockPilot.Api.Models;

public record LoginResponse(string Token, DateTime ExpiresAtUtc, string RefreshToken);
