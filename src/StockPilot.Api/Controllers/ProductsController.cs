using Microsoft.AspNetCore.Mvc;
using StockPilot.Api.Models;

namespace StockPilot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductStore _productStore;

    public ProductsController(IProductStore productStore)
    {
        _productStore = productStore;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductDto>>> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var allProducts = await _productStore.GetAllAsync(cancellationToken);
        var products = allProducts.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            products = products.Where(p =>
                p.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                p.Sku.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        products = sortBy?.ToLowerInvariant() switch
        {
            "price" => products.OrderBy(p => p.Price),
            "name" => products.OrderBy(p => p.Name),
            "sku" => products.OrderBy(p => p.Sku),
            _ => products.OrderBy(p => p.Id)
        };

        var totalCount = products.Count();
        var pagedProducts = products.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var dtos = pagedProducts.Select(ToDto).ToList();

        var result = new PagedResult<ProductDto>(dtos, page, pageSize, totalCount);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ProductDto>> GetById(int id, CancellationToken cancellationToken = default)
    {
        var product = await _productStore.GetByIdAsync(id, cancellationToken);
        if (product == null)
        {
            return NotFound();
        }

        return Ok(ToDto(product));
    }

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = new Product(request.Sku, request.Name, request.Price);
        await _productStore.AddAsync(product, cancellationToken);

        var dto = ToDto(product);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        var removed = await _productStore.RemoveAsync(id, cancellationToken);
        if (!removed)
        {
            return NotFound();
        }

        return NoContent();
    }

    private static ProductDto ToDto(Product product) =>
        new(product.Id, product.Sku, product.Name, product.Price);
}
