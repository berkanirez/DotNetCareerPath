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
    public void GetAll_ReturnsThreeSeededProducts()
    {
        var controller = CreateController();

        var result = controller.GetAll();

        var page = Assert.IsType<PagedResult<ProductDto>>(((OkObjectResult)result.Result!).Value);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(3, page.Items.Count);
    }

    [Fact]
    public void GetById_ExistingId_ReturnsProduct()
    {
        var controller = CreateController();

        var result = controller.GetById(1);

        var product = Assert.IsType<ProductDto>(((OkObjectResult)result.Result!).Value);
        Assert.Equal("SKU-001", product.Sku);
    }

    [Fact]
    public void GetById_MissingId_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = controller.GetById(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public void Create_ValidRequest_ReturnsCreatedAtActionWithLocationAndAddsProduct()
    {
        var controller = CreateController();
        var request = new CreateProductRequest("SKU-004", "Webcam", 45.00m);

        var result = controller.Create(request);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(ProductsController.GetById), created.ActionName);
        var dto = Assert.IsType<ProductDto>(created.Value);
        Assert.Equal(4, dto.Id);

        var afterCreate = controller.GetAll();
        var page = Assert.IsType<PagedResult<ProductDto>>(((OkObjectResult)afterCreate.Result!).Value);
        Assert.Equal(4, page.TotalCount);
    }

    [Fact]
    public void Create_OnASeparateTest_AlsoAssignsIdFour()
    {
        // This test would fail (see the reverted NaiveAttempt.cs experiment
        // earlier today) if both Create tests shared one static product list.
        // They don't — each test gets its own InMemoryProductStore.
        var controller = CreateController();

        var result = controller.Create(new CreateProductRequest("SKU-005", "Headset", 55.00m));

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<ProductDto>(created.Value);
        Assert.Equal(4, dto.Id);
    }

    [Fact]
    public void Delete_ExistingId_RemovesProductAndReturnsNoContent()
    {
        var controller = CreateController();

        var result = controller.Delete(2);

        Assert.IsType<NoContentResult>(result);
        var afterDelete = controller.GetAll();
        var page = Assert.IsType<PagedResult<ProductDto>>(((OkObjectResult)afterDelete.Result!).Value);
        Assert.DoesNotContain(page.Items, p => p.Id == 2);
    }

    [Fact]
    public void Delete_MissingId_ReturnsNotFound()
    {
        var controller = CreateController();

        var result = controller.Delete(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void Create_CalledTwiceOnSameStore_AssignsSequentialIds ()
    {
        var controller = CreateController();

        var result1 = controller.Create(new CreateProductRequest("SKU-006", "Item 6", 10.00m));
        var result2 = controller.Create(new CreateProductRequest("SKU-007", "Item 7", 20.00m));
        
        var created1 = Assert.IsType<CreatedAtActionResult>(result1.Result);
        var dto1 = Assert.IsType<ProductDto>(created1.Value);
        Assert.Equal(4, dto1.Id);

        var created2 = Assert.IsType<CreatedAtActionResult>(result2.Result);
        var dto2 = Assert.IsType<ProductDto>(created2.Value);
        Assert.Equal(5, dto2.Id);
    }
}
