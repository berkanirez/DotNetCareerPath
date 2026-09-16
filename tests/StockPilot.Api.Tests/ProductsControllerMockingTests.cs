using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using StockPilot.Api.Controllers;
using StockPilot.Api.Models;

namespace StockPilot.Api.Tests;

// Unlike ProductsControllerTests.cs (which uses the real, hand-written
// InMemoryProductStore fake), these tests use Moq to simulate a store
// behavior InMemoryProductStore structurally can never produce: a real
// DbUpdateException thrown mid-race by the database's own unique index
// (Day 19's Layer 2, honestly flagged as "live-proven only" ever since).
//
// This mocks our OWN IProductStore abstraction, not EF Core itself — per
// CLAUDE.md's rule against mocking EF Core just to make a test pass.
public class ProductsControllerMockingTests
{
    [Fact]
    public async Task Create_StoreThrowsDbUpdateException_ReturnsConflict()
    {
        var mockStore = new Mock<IProductStore>();

        // Layer 1 (the proactive check) must pass so execution actually
        // reaches AddAsync — otherwise Create would short-circuit at Layer 1,
        // and Layer 2's catch block (what this test targets) would never run.
        // (Verified live: Moq happens to default an unconfigured Task<bool>
        // method to a completed Task wrapping `false` anyway, so this specific
        // Setup isn't strictly load-bearing here — kept anyway so the test's
        // real assumption is explicit, not silently riding on a Moq default.)
        mockStore
            .Setup(s => s.SkuExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        mockStore
            .Setup(s => s.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Simulated unique index violation from a concurrent request."));

        var controller = new ProductsController(mockStore.Object);
        var request = new CreateProductRequest("SKU-RACE", "Mocked Race Condition Product", 10.00m);

        var result = await controller.Create(request);

        Assert.IsType<ConflictObjectResult>(result.Result);
    }
}
