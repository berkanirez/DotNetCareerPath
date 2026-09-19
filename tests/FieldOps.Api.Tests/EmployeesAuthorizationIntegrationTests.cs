using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FieldOps.Api.Tests;

// Unlike EmployeeApplicationServiceTests (Day 34), which call the service
// directly with zero HTTP involved, these tests go through a real HttpClient
// against a real running copy of the app — the only way to actually prove
// [FromHeader]'s behavior, since that's a middleware/model-binding concern
// EmployeeApplicationServiceTests structurally cannot see.
//
// Note: IOrganizationDirectory/IEmployeeDirectory are registered Singleton,
// so their in-memory state is SHARED across every test in this class (one
// WebApplicationFactory instance backs the whole class). Each test uses a
// unique employee name to avoid any test depending on another's leftover
// data — the same discipline StockPilot's Day 27 unique-SKU convention used,
// for a different underlying reason (shared memory here, a shared real
// database there).
public class EmployeesAuthorizationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EmployeesAuthorizationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAll_NoOrganizationHeader_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/employees");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_NoOrganizationHeader_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/employees", new { Name = "Should Never Exist" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ByAdmin_ReturnsCreated()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        client.DefaultRequestHeaders.Add("X-Employee-Id", "1"); // seeded Org1 Admin

        var response = await client.PostAsJsonAsync("/api/employees", new { Name = "Should Never Exist" });
        var dto = await response.Content.ReadFromJsonAsync<EmployeeDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, dto!.OrganizationId);
    }

    [Fact]
    public async Task Create_NonExistentOrganization_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Organization-Id", "999");
        client.DefaultRequestHeaders.Add("X-Employee-Id", "1"); // seeded Org1 Admin — isolates this test to the org-existence check only

        var response = await client.PostAsJsonAsync("/api/employees", new { Name = "Ghost Employee" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_ScopedToOrganization_NeverReturnsAnotherOrganizationsEmployees()
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var org1Client = _factory.CreateClient();
        org1Client.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1Client.DefaultRequestHeaders.Add("X-Employee-Id", "1"); // seeded Org1 Admin
        var org2Client = _factory.CreateClient();
        org2Client.DefaultRequestHeaders.Add("X-Organization-Id", "2");

        var createResponse = await org1Client.PostAsJsonAsync("/api/employees", new { Name = $"Org1-Only-{uniqueSuffix}" });
        createResponse.EnsureSuccessStatusCode();

        var org1Employees = await org1Client.GetFromJsonAsync<List<EmployeeDto>>("/api/employees");
        var org2Employees = await org2Client.GetFromJsonAsync<List<EmployeeDto>>("/api/employees");

        Assert.Contains(org1Employees!, e => e.Name == $"Org1-Only-{uniqueSuffix}");
        Assert.DoesNotContain(org2Employees!, e => e.Name == $"Org1-Only-{uniqueSuffix}");
    }

    private record EmployeeDto(int Id, string Name, int OrganizationId);
}
