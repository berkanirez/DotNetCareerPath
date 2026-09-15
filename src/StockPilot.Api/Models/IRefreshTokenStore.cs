namespace StockPilot.Api.Models;

public interface IRefreshTokenStore
{
    string Issue(string username);
    bool TryConsume(string refreshToken, out string username);
}
