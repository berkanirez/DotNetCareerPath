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
    public ActionResult<PagedResult<ProductDto>> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var products = _productStore.GetAll().AsEnumerable();

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
    public ActionResult<ProductDto> GetById(int id)
    {
        var product = _productStore.GetById(id);
        if (product == null)
        {
            return NotFound();
        }

        return Ok(ToDto(product));
    }

    [HttpPost]
    public ActionResult<ProductDto> Create(CreateProductRequest request)
    {
        var product = new Product(request.Sku, request.Name, request.Price);
        _productStore.Add(product);

        var dto = ToDto(product);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var removed = _productStore.Remove(id);
        if (!removed)
        {
            return NotFound();
        }

        return NoContent();
    }

    private static ProductDto ToDto(Product product) =>
        new(product.Id, product.Sku, product.Name, product.Price);
}
