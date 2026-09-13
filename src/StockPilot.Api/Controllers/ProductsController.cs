using Microsoft.AspNetCore.Mvc;
using StockPilot.Api.Models;

namespace StockPilot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private static readonly List<Product> Products = new()
    {
        new Product("SKU-001", "Wireless Mouse", 19.99m) { Id = 1 },
        new Product("SKU-002", "Mechanical Keyboard", 79.99m) { Id = 2 },
        new Product("SKU-003", "USB-C Hub", 34.50m) { Id = 3 }
    };

    // TEMPORARY — not thread-safe (a real race is possible under concurrent
    // POSTs). Acceptable only because Week 4 replaces this in-memory store
    // with EF Core + SQL Server's own IDENTITY column.
    private static int _nextId = 4;

    [HttpGet]
    public ActionResult<IReadOnlyList<ProductDto>> GetAll()
    {
        var dtos = Products.Select(ToDto).ToList();
        return Ok(dtos);
    }

    [HttpGet("{id}")]
    public ActionResult<ProductDto> GetById(int id)
    {
        var product = Products.FirstOrDefault(p => p.Id == id);
        if (product == null)
        {
            return NotFound();
        }

        return Ok(ToDto(product));
    }

    [HttpPost]
    public ActionResult<ProductDto> Create(CreateProductRequest request)
    {
        var product = new Product(request.Sku, request.Name, request.Price)
        {
            Id = _nextId++
        };

        Products.Add(product);

        var dto = ToDto(product);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var product = Products.FirstOrDefault(p => p.Id == id);
        if (product == null)
        {
            return NotFound();
        }

        Products.Remove(product);
        return NoContent();
    }

    private static ProductDto ToDto(Product product) =>
        new(product.Id, product.Sku, product.Name, product.Price);
}
