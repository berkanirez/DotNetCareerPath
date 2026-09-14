using System.ComponentModel.DataAnnotations;

namespace StockPilot.Api.Models;

public record UpdateProductRequest(
    [Required, StringLength(200)] string Name,
    [Range(0.01, double.MaxValue)] decimal Price,
    [Required] byte[] RowVersion);
