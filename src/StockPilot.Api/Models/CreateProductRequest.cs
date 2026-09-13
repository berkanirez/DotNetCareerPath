using System.ComponentModel.DataAnnotations;

namespace StockPilot.Api.Models;

public record CreateProductRequest(
    [Required, MinLength(2), StringLength(50)] string Sku,
    [Required, StringLength(200)] string Name,
    [Range(0.01, double.MaxValue)] decimal Price);
