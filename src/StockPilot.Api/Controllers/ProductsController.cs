using Microsoft.AspNetCore.Mvc;
using StockPilot.Api.Models;

namespace StockPilot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private static readonly List<ProductDto> Products = new()
    {
        new ProductDto(1, "SKU-001", "Wireless Mouse", 19.99m),
        new ProductDto(2, "SKU-002", "Mechanical Keyboard", 79.99m),
        new ProductDto(3, "SKU-003", "USB-C Hub", 34.50m)
    };

    [HttpGet]
    public ActionResult<IReadOnlyList<ProductDto>> GetAll()
    {
        return Ok(Products);
    }

    [HttpGet("{id}")]
    public ActionResult<ProductDto> GetById(int id)
    {
        var product = Products.FirstOrDefault(p => p.Id == id);
        if (product == null)
        {
            return NotFound();
        }
        return Ok(product);
    }
}
