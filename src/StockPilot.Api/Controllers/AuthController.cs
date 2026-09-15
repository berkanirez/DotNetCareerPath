using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using StockPilot.Api.Models;

namespace StockPilot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    // A single hardcoded demo account — today's deliberate simplification.
    // Production requires a real user store (a Users table, a registration
    // flow, one hashed password per real user), not a constant baked into
    // the API. The password itself is still hashed, not compared as plain
    // text, so at least that part is realistic.
    private const string DemoUsername = "admin";
    private static readonly PasswordHasher<object> PasswordHasher = new();
    private static readonly string DemoPasswordHash = PasswordHasher.HashPassword(null!, "Passw0rd!");

    private readonly IConfiguration _configuration;
    private readonly IRefreshTokenStore _refreshTokenStore;

    public AuthController(IConfiguration configuration, IRefreshTokenStore refreshTokenStore)
    {
        _configuration = configuration;
        _refreshTokenStore = refreshTokenStore;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public ActionResult<LoginResponse> Login(LoginRequest request)
    {
        var isValidPassword = request.Username == DemoUsername &&
            PasswordHasher.VerifyHashedPassword(null!, DemoPasswordHash, request.Password) == PasswordVerificationResult.Success;

        if (!isValidPassword)
        {
            return Unauthorized("Invalid username or password.");
        }

        var (accessToken, expiresAtUtc) = GenerateAccessToken(request.Username);
        var refreshToken = _refreshTokenStore.Issue(request.Username);

        return Ok(new LoginResponse(accessToken, expiresAtUtc, refreshToken));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public ActionResult<LoginResponse> Refresh(RefreshTokenRequest request)
    {
        // TryConsume removes the token the moment it's looked up — whether
        // this call succeeds or the token turns out to be expired, that
        // exact refresh token can never be presented successfully again.
        if (!_refreshTokenStore.TryConsume(request.RefreshToken, out var username))
        {
            return Unauthorized("Invalid or already-used refresh token.");
        }

        var (accessToken, expiresAtUtc) = GenerateAccessToken(username);
        // Rotation: a brand-new refresh token replaces the one just consumed,
        // rather than letting the same refresh token be reused indefinitely.
        var newRefreshToken = _refreshTokenStore.Issue(username);

        return Ok(new LoginResponse(accessToken, expiresAtUtc, newRefreshToken));
    }

    private (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(string username)
    {
        var jwtSection = _configuration.GetSection("Jwt");
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
        var signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var expiryMinutes = int.Parse(jwtSection["ExpiryMinutes"]!);
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: signingCredentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
