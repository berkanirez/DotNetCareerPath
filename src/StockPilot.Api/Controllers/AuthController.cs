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
    // Two hardcoded demo accounts (one per role) — still today's deliberate
    // simplification, extended from Day 23's single account only because a
    // real role check needs at least two different roles to actually prove
    // anything. Production requires a real user store (a Users table) where
    // role is a real, assignable column, not a constant here.
    private static readonly PasswordHasher<object> PasswordHasher = new();
    private static readonly Dictionary<string, (string PasswordHash, string Role)> DemoUsers = new()
    {
        ["admin"] = (PasswordHasher.HashPassword(null!, "Passw0rd!"), "Admin"),
        ["employee"] = (PasswordHasher.HashPassword(null!, "Employee123!"), "Employee")
    };

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
        var isValidLogin = DemoUsers.TryGetValue(request.Username, out var user) &&
            PasswordHasher.VerifyHashedPassword(null!, user.PasswordHash, request.Password) == PasswordVerificationResult.Success;

        if (!isValidLogin)
        {
            return Unauthorized("Invalid username or password.");
        }

        var (accessToken, expiresAtUtc) = GenerateAccessToken(request.Username, user.Role);
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

        // The refresh token store only remembers a username, not a role —
        // the role has to be looked up again here, the same way Login did.
        var role = DemoUsers[username].Role;
        var (accessToken, expiresAtUtc) = GenerateAccessToken(username, role);
        // Rotation: a brand-new refresh token replaces the one just consumed,
        // rather than letting the same refresh token be reused indefinitely.
        var newRefreshToken = _refreshTokenStore.Issue(username);

        return Ok(new LoginResponse(accessToken, expiresAtUtc, newRefreshToken));
    }

    private (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(string username, string role)
    {
        var jwtSection = _configuration.GetSection("Jwt");
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
        var signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var expiryMinutes = int.Parse(jwtSection["ExpiryMinutes"]!);
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            // ClaimTypes.Role specifically — not a made-up string like "role" —
            // because ASP.NET Core's [Authorize(Roles = "...")] checks a
            // request's identity against exactly this claim type by default.
            new Claim(ClaimTypes.Role, role)
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
