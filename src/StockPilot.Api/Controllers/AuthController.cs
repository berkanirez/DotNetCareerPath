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

    public AuthController(IConfiguration configuration)
    {
        _configuration = configuration;
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

        var jwtSection = _configuration.GetSection("Jwt");
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
        var signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var expiryMinutes = int.Parse(jwtSection["ExpiryMinutes"]!);
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, request.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: signingCredentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        return Ok(new LoginResponse(tokenString, expiresAtUtc));
    }
}
