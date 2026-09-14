using Microsoft.AspNetCore.Mvc;
using StockPilot.Api.Controllers;
using StockPilot.Api.Models;

namespace StockPilot.Api.Tests;

public class ProductsControllerTests
{
    // Every test builds its own fresh store — no shared/static state,
    // so tests are fully isolated from each other regardless of order.
    private static ProductsController CreateController() => new(new InMemoryProductStore());

    [Fact]
    public async Task GetAll_ReturnsThreeSeededProducts()
    {
        var controller = CreateController();

        var result = await controller.GetAll();

        var page = Assert.IsType<PagedResult<ProductDto>>(((OkObjectResult)result.Result!).Value);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(3, page.Items.Count);
    }

    [Fact]
    public async Task GetById_ExistingId_ReturnsProduct()
    {
        var controller = CreateController();

        var result = await controller.GetById(1);

        var product = Assert.IsType<ProductDto>(((OkObjectResult)result.Result!).Value);
        Assert.Equal("SKU-001", product.Sku);
    }

    [Fact]
    public async Task GetById_MissingId_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = await controller.GetById(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_ValidRequest_ReturnsCreatedAtActionWithLocationAndAddsProduct()
    {
        var controller = CreateController();
        var request = new CreateProductRequest("SKU-004", "Webcam", 45.00m);

        var result = await controller.Create(request);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(ProductsController.GetById), created.ActionName);
        var dto = Assert.IsType<ProductDto>(created.Value);
        Assert.Equal(4, dto.Id);

        var afterCreate = await controller.GetAll();
        var page = Assert.IsType<PagedResult<ProductDto>>(((OkObjectResult)afterCreate.Result!).Value);
        Assert.Equal(4, page.TotalCount);
    }

    [Fact]
    public async Task Create_OnASeparateTest_AlsoAssignsIdFour()
    {
        // This test would fail (see the reverted NaiveAttempt.cs experiment
        // earlier today) if both Create tests shared one static product list.
        // They don't — each test gets its own InMemoryProductStore.
        var controller = CreateController();

        var result = await controller.Create(new CreateProductRequest("SKU-005", "Headset", 55.00m));

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<ProductDto>(created.Value);
        Assert.Equal(4, dto.Id);
    }

    [Fact]
    public async Task Delete_ExistingId_RemovesProductAndReturnsNoContent()
    {
        var controller = CreateController();

        var result = await controller.Delete(2);

        Assert.IsType<NoContentResult>(result);
        var afterDelete = await controller.GetAll();
        var page = Assert.IsType<PagedResult<ProductDto>>(((OkObjectResult)afterDelete.Result!).Value);
        Assert.DoesNotContain(page.Items, p => p.Id == 2);
    }

    [Fact]
    public async Task Delete_MissingId_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = await controller.Delete(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Create_CalledTwiceOnSameStore_AssignsSequentialIds()
    {
        var controller = CreateController();

        var result1 = await controller.Create(new CreateProductRequest("SKU-006", "Item 6", 10.00m));
        var result2 = await controller.Create(new CreateProductRequest("SKU-007", "Item 7", 20.00m));

        var created1 = Assert.IsType<CreatedAtActionResult>(result1.Result);
        var dto1 = Assert.IsType<ProductDto>(created1.Value);
        Assert.Equal(4, dto1.Id);

        var created2 = Assert.IsType<CreatedAtActionResult>(result2.Result);
        var dto2 = Assert.IsType<ProductDto>(created2.Value);
        Assert.Equal(5, dto2.Id);
    }

    [Fact]
    public async Task GetBySearch_ReturnsMatchingProducts()
    {
        var controller = CreateController();

        var result = await controller.GetAll(search: "mouse");

        var page = Assert.IsType<PagedResult<ProductDto>>(((OkObjectResult)result.Result!).Value);
        Assert.Single(page.Items);
        Assert.Equal("Wireless Mouse", page.Items[0].Name);
    }

    [Fact]
    public async Task Create_DuplicateSku_ReturnsConflict()
    {
        // Uses InMemoryProductStore, so this only exercises Layer 1 (the
        // proactive SkuExistsAsync check) — it can never reach Layer 2's
        // DbUpdateException catch, since InMemoryProductStore never throws one.
        var controller = CreateController();

        var duplicateRequest = new CreateProductRequest("SKU-001", "Another Mouse", 25.00m);
        var result = await controller.Create(duplicateRequest);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_NonDuplicateSku_StillSucceeds()
    {
        var controller = CreateController();

        var request = new CreateProductRequest("SKU-010", "New Product", 30.00m);
        var result = await controller.Create(request);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<ProductDto>(created.Value);
        Assert.Equal("SKU-010", dto.Sku);
    }

    [Fact]
    public async Task BulkCreate_ValidRequests_AddsAllProducts()
    {
        // InMemoryProductStore's AddRangeAsync has no real transaction/rollback
        // concept and no duplicate-SKU check, so only the all-succeed path is
        // testable here — the actual atomic-rollback behavior is real only
        // against EfProductStore and was verified live, not via this test.
        var controller = CreateController();
        var requests = new List<CreateProductRequest>
        {
            new("SKU-011", "Batch Item 1", 11.00m),
            new("SKU-012", "Batch Item 2", 12.00m)
        };

        var result = await controller.BulkCreate(requests);

        var dtos = Assert.IsAssignableFrom<IReadOnlyList<ProductDto>>(((ObjectResult)result.Result!).Value);
        Assert.Equal(201, ((ObjectResult)result.Result!).StatusCode);
        Assert.Equal(2, dtos.Count);

        var afterBulk = await controller.GetAll();
        var page = Assert.IsType<PagedResult<ProductDto>>(((OkObjectResult)afterBulk.Result!).Value);
        Assert.Equal(5, page.TotalCount);
    }

    [Fact]
    public async Task Update_ExistingId_ReturnsUpdatedProduct()
    {
        // InMemoryProductStore ignores rowVersion entirely (no real database
        // underneath it, so no real concurrency token to check) — this test
        // only proves the ordinary, non-conflicting update path.
        var controller = CreateController();
        var request = new UpdateProductRequest("Updated Mouse", 29.99m, new byte[] { 1 });

        var result = await controller.Update(1, request);

        var dto = Assert.IsType<ProductDto>(((OkObjectResult)result.Result!).Value);
        Assert.Equal("Updated Mouse", dto.Name);
        Assert.Equal(29.99m, dto.Price);
        Assert.Equal("SKU-001", dto.Sku); // UpdateProductRequest carries no Sku, so it must stay untouched.
    }

    [Fact]
    public async Task Update_MissingId_ReturnsNotFound()
    {
        var controller = CreateController();
        var request = new UpdateProductRequest("Doesn't matter", 10.00m, new byte[] { 1 });

        var result = await controller.Update(999, request);

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
