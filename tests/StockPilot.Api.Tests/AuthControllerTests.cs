using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using StockPilot.Api.Controllers;
using StockPilot.Api.Models;

namespace StockPilot.Api.Tests;

public class AuthControllerTests
{
    // A minimal, real IConfiguration built in-memory (not mocked) — the same
    // "Jwt" keys AuthController reads from appsettings.Development.json in
    // the real app, just with test-only values.
    private static AuthController CreateController()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience",
                ["Jwt:Key"] = "test-only-signing-key-at-least-32-characters-long",
                ["Jwt:ExpiryMinutes"] = "60"
            })
            .Build();

        return new AuthController(configuration, new InMemoryRefreshTokenStore());
    }

    [Theory]
    [InlineData("admin", "Passw0rd!", "Admin")]
    [InlineData("employee", "Employee123!", "Employee")]
    public void Login_ValidCredentials_IssuesTokenWithCorrectRoleClaim(string username, string password, string expectedRole)
    {
        var controller = CreateController();
        var request = new LoginRequest(username, password);

        var result = controller.Login(request);

        var response = Assert.IsType<LoginResponse>(((OkObjectResult)result.Result!).Value);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(response.Token);
        var roleClaim = Assert.Single(jwt.Claims, c => c.Type == ClaimTypes.Role);
        Assert.Equal(expectedRole, roleClaim.Value);
    }

    [Fact]
    public void Login_WrongPassword_ReturnsUnauthorized()
    {
        var controller = CreateController();
        var request = new LoginRequest("admin", "not-the-real-password");

        var result = controller.Login(request);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public void Login_UnknownUsername_ReturnsUnauthorized()
    {
        var controller = CreateController();
        var request = new LoginRequest("nobody", "irrelevant");

        var result = controller.Login(request);

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }
}
