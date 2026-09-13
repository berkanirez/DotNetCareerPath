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
    public ActionResult<IReadOnlyList<ProductDto>> GetAll()
    {
        var dtos = _productStore.GetAll().Select(ToDto).ToList();
        return Ok(dtos);
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
