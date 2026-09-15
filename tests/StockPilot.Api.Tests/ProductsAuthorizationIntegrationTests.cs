using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using StockPilot.Api.Models;

namespace StockPilot.Api.Tests;

// Unlike every other test in this project, these go through the REAL HTTP
// pipeline (routing, UseAuthentication, UseAuthorization) via a real running
// copy of the app (WebApplicationFactory<Program>) — the honest gap noted
// since Day 23 ("we can prove [Authorize] works live via curl, but no
// automated test exercises the middleware pipeline") closes here.
//
// Deliberate scope choice for today: this runs against the real StockPilotDb
// (same appsettings.Development.json the app always uses) rather than an
// isolated test database — that's a separate, later Week 6 topic
// (test-database isolation / Testcontainers). Each test creates its own
// uniquely-SKU'd throwaway product rather than touching real seeded data.
public class ProductsAuthorizationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ProductsAuthorizationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Delete_NoToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.DeleteAsync("/api/products/999999");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_NoToken_ReturnsOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Delete_EmployeeToken_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        var employeeToken = await LoginAsync(client, "employee", "Employee123!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", employeeToken);

        var response = await client.DeleteAsync("/api/products/999999");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_AdminToken_DeletesRealProductThroughTheRealPipeline()
    {
        var client = _factory.CreateClient();
        var adminToken = await LoginAsync(client, "admin", "Passw0rd!");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // A unique SKU per run means repeated test executions never collide
        // with each other or with real seeded data in the shared dev database.
        // Kept under CreateProductRequest's [StringLength(50)] on Sku — a
        // longer prefix + a full GUID (32 chars) genuinely exceeded it,
        // caught live as a real 400 Bad Request the first time this ran.
        var uniqueSku = $"SKU-IT-{Guid.NewGuid():N}"[..30];
        var createResponse = await client.PostAsJsonAsync(
            "/api/products",
            new CreateProductRequest(uniqueSku, "Integration Test Product", 1.00m));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ProductDto>();

        var deleteResponse = await client.DeleteAsync($"/api/products/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return login!.Token;
    }
}
