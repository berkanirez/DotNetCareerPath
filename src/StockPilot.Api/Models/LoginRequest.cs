using System.ComponentModel.DataAnnotations;

namespace StockPilot.Api.Models;

public record LoginRequest(
    [Required] string Username,
    [Required] string Password);
