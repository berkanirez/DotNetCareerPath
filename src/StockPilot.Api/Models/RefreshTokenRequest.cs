using System.ComponentModel.DataAnnotations;

namespace StockPilot.Api.Models;

public record RefreshTokenRequest([Required] string RefreshToken);
