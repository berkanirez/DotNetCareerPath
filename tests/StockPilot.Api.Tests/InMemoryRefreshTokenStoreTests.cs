using StockPilot.Api.Models;

namespace StockPilot.Api.Tests;

public class InMemoryRefreshTokenStoreTests
{
    [Fact]
    public void TryConsume_ValidToken_ReturnsTrueAndUsername()
    {
        var store = new InMemoryRefreshTokenStore();
        var token = store.Issue("admin");

        var result = store.TryConsume(token, out var username);

        Assert.True(result);
        Assert.Equal("admin", username);
    }

    [Fact]
    public void TryConsume_UnknownToken_ReturnsFalse()
    {
        var store = new InMemoryRefreshTokenStore();

        var result = store.TryConsume("this-token-was-never-issued", out var username);

        Assert.False(result);
        Assert.Equal(string.Empty, username);
    }

    [Fact]
    public void TryConsume_SameTokenTwice_SecondCallReturnsFalse()
    {
        // Proves rotation: TryRemove deletes on first read, so a second
        // attempt with the identical token finds nothing.
        var store = new InMemoryRefreshTokenStore();
        var token = store.Issue("admin");

        var firstAttempt = store.TryConsume(token, out _);
        var secondAttempt = store.TryConsume(token, out var usernameOnSecondAttempt);

        Assert.True(firstAttempt);
        Assert.False(secondAttempt);
        Assert.Equal(string.Empty, usernameOnSecondAttempt);
    }

    [Fact]
    public void TryConsume_ExpiredToken_ReturnsFalse()
    {
        // A negative lifetime means the token is already expired the instant
        // Issue() creates it — no real waiting needed to prove this path.
        var store = new InMemoryRefreshTokenStore(TimeSpan.FromSeconds(-1));
        var token = store.Issue("admin");

        var result = store.TryConsume(token, out var username);

        Assert.False(result);
        Assert.Equal(string.Empty, username);
    }
}
