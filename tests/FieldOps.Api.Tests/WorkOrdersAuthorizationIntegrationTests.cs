using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FieldOps.Api.Tests;

// Day 40: WorkOrdersController applies the same membership pattern
// EmployeesController arrived at only after three live-found vulnerabilities
// (Days 35, 38, 39) — here it's correct from the start. These tests prove
// that directly, rather than proving a fix for something that was broken.
public class WorkOrdersAuthorizationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WorkOrdersAuthorizationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetAll_NoOrganizationHeader_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/workorders");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_NoEmployeeHeader_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Organization-Id", "1");

        var response = await client.GetAsync("/api/workorders");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_ByEmployeeFromAnotherOrganization_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Organization-Id", "2");
        client.DefaultRequestHeaders.Add("X-Employee-Id", "1"); // seeded Org1 Admin, targeting Org 2

        var response = await client.GetAsync("/api/workorders");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_ThenGetAll_ScopedToOwnOrganization()
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var org1Client = _factory.CreateClient();
        org1Client.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1Client.DefaultRequestHeaders.Add("X-Employee-Id", "1"); // seeded Org1 Admin
        var org2Client = _factory.CreateClient();
        org2Client.DefaultRequestHeaders.Add("X-Organization-Id", "2");
        org2Client.DefaultRequestHeaders.Add("X-Employee-Id", "3"); // seeded Org2 Admin

        var createResponse = await org1Client.PostAsJsonAsync("/api/workorders", new { Title = $"Org1-Only-{uniqueSuffix}" });
        var dto = await createResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal(1, dto!.OrganizationId);

        var org1WorkOrders = await org1Client.GetFromJsonAsync<List<WorkOrderDto>>("/api/workorders");
        var org2WorkOrders = await org2Client.GetFromJsonAsync<List<WorkOrderDto>>("/api/workorders");

        Assert.Contains(org1WorkOrders!, w => w.Title == $"Org1-Only-{uniqueSuffix}");
        Assert.DoesNotContain(org2WorkOrders!, w => w.Title == $"Org1-Only-{uniqueSuffix}");
    }

    private record WorkOrderDto(int Id, string Title, int OrganizationId, int Status);
}
