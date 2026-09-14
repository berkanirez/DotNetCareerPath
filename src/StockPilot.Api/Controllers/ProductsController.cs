using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        var duplicateSkuMessage = $"A product with SKU '{request.Sku}' already exists.";

        // Layer 1 (proactive): the common case — reject an obvious duplicate
        // before ever touching the database's write path.
        if (await _productStore.SkuExistsAsync(request.Sku, cancellationToken))
        {
            return Conflict(duplicateSkuMessage);
        }

        var product = new Product(request.Sku, request.Name, request.Price);

        try
        {
            await _productStore.AddAsync(product, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Layer 2 (reactive safety net): a rare race — two concurrent
            // requests both passed the check above before either committed.
            // The database's own unique index is the final authority.
            return Conflict(duplicateSkuMessage);
        }

        var dto = ToDto(product);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    [HttpPost("bulk")]
    public async Task<ActionResult<IReadOnlyList<ProductDto>>> BulkCreate(List<CreateProductRequest> requests, CancellationToken cancellationToken = default)
    {
        var products = requests.Select(r => new Product(r.Sku, r.Name, r.Price)).ToList();

        try
        {
            await _productStore.AddRangeAsync(products, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Same unique-index violation as a single Create's Layer 2 — the
            // difference here is that AddRangeAsync's transaction guarantees
            // NONE of this batch was saved, not just the one that failed.
            return Conflict("One or more products in this batch could not be added (e.g. a duplicate SKU) — the entire batch was rolled back, nothing was saved.");
        }

        // A single Location header doesn't make sense for multiple created
        // resources, so 201 is returned directly with the full list rather
        // than via CreatedAtAction (which assumes exactly one resource).
        var dtos = products.Select(ToDto).ToList();
        return StatusCode(StatusCodes.Status201Created, dtos);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ProductDto>> Update(int id, UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var product = await _productStore.UpdateAsync(id, request.Name, request.Price, request.RowVersion, cancellationToken);
            if (product is null)
            {
                return NotFound();
            }

            return Ok(ToDto(product));
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone else updated (or deleted) this product between our
            // client's last read and this request — the RowVersion it sent
            // no longer matches what's in the database. Not a server fault:
            // the client's data is stale, so 409 (not 500) is correct, same
            // reasoning as Day 19's duplicate-SKU Conflict responses.
            return Conflict("The product was modified by another request since it was last read. Reload it and try again.");
        }
    }

    // First protected endpoint in either codebase — deleting a product now
    // requires a valid, signed JWT (see AuthController.Login). Every other
    // action here is still deliberately open today; expanding [Authorize]
    // coverage and adding roles/policies is Week 5's remaining scope.
    [Authorize]
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
        new(product.Id, product.Sku, product.Name, product.Price, product.RowVersion);
}
