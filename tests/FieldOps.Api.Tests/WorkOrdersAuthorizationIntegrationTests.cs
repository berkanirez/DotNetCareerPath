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

    [Fact]
    public async Task Assign_ByAdmin_TransitionsToAssigned()
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var org1Client = _factory.CreateClient();
        org1Client.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1Client.DefaultRequestHeaders.Add("X-Employee-Id", "1"); // seeded Org1 Admin

        var createResponse = await org1Client.PostAsJsonAsync("/api/workorders", new { Title = $"Assign-Me-{uniqueSuffix}" });
        var created = await createResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        var assignResponse = await org1Client.PostAsJsonAsync($"/api/workorders/{created!.Id}/assign", new { EmployeeId = 2 }); // seeded Org1 Member
        var assigned = await assignResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);
        Assert.Equal(1, assigned!.Status); // WorkOrderStatus.Assigned == 1
        Assert.Equal(2, assigned.AssignedEmployeeId);
    }

    [Fact]
    public async Task Assign_ByMember_ReturnsForbidden()
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");

        var createResponse = await org1AdminClient.PostAsJsonAsync("/api/workorders", new { Title = $"Member-Cannot-Assign-{uniqueSuffix}" });
        var created = await createResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        var org1MemberClient = _factory.CreateClient();
        org1MemberClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1MemberClient.DefaultRequestHeaders.Add("X-Employee-Id", "2"); // seeded Org1 Member

        var assignResponse = await org1MemberClient.PostAsJsonAsync($"/api/workorders/{created!.Id}/assign", new { EmployeeId = 2 });

        Assert.Equal(HttpStatusCode.Forbidden, assignResponse.StatusCode);
    }

    [Fact]
    public async Task Assign_AlreadyAssigned_ReturnsBadRequest()
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var org1Client = _factory.CreateClient();
        org1Client.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1Client.DefaultRequestHeaders.Add("X-Employee-Id", "1");

        var createResponse = await org1Client.PostAsJsonAsync("/api/workorders", new { Title = $"Only-Once-{uniqueSuffix}" });
        var created = await createResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        var firstAssign = await org1Client.PostAsJsonAsync($"/api/workorders/{created!.Id}/assign", new { EmployeeId = 2 });
        firstAssign.EnsureSuccessStatusCode();

        var secondAssign = await org1Client.PostAsJsonAsync($"/api/workorders/{created.Id}/assign", new { EmployeeId = 2 });

        Assert.Equal(HttpStatusCode.BadRequest, secondAssign.StatusCode);
    }

    [Fact]
    public async Task Assign_ToEmployeeFromAnotherOrganization_ReturnsBadRequest()
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var org1Client = _factory.CreateClient();
        org1Client.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1Client.DefaultRequestHeaders.Add("X-Employee-Id", "1");

        var createResponse = await org1Client.PostAsJsonAsync("/api/workorders", new { Title = $"Cross-Org-Assignee-{uniqueSuffix}" });
        var created = await createResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        var assignResponse = await org1Client.PostAsJsonAsync($"/api/workorders/{created!.Id}/assign", new { EmployeeId = 4 }); // seeded Org2 Member

        Assert.Equal(HttpStatusCode.BadRequest, assignResponse.StatusCode);
    }

    // Day 41: applies Day 37/38's information-disclosure lesson from the
    // start — an Org 2 Admin targeting Org 1's work order should see the
    // exact same "does not exist" outcome as a genuinely missing id, never
    // a distinguishable error revealing that work order 1 belongs to someone
    // else's tenant.
    [Fact]
    public async Task Assign_WorkOrderFromAnotherOrganization_ReturnsBadRequest()
    {
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var org1Client = _factory.CreateClient();
        org1Client.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1Client.DefaultRequestHeaders.Add("X-Employee-Id", "1");

        var createResponse = await org1Client.PostAsJsonAsync("/api/workorders", new { Title = $"Org1-Private-{uniqueSuffix}" });
        var created = await createResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        var org2Client = _factory.CreateClient();
        org2Client.DefaultRequestHeaders.Add("X-Organization-Id", "2");
        org2Client.DefaultRequestHeaders.Add("X-Employee-Id", "3"); // seeded Org2 Admin

        // Deliberately targets employee id 2 — Org 1's OWN Member. If the
        // work-order-organization check were missing, this would otherwise
        // succeed (employee 2 genuinely belongs to Org 1, so the separate
        // employee-organization check alone wouldn't catch it), which is
        // exactly what makes this test an isolated proof of THAT check,
        // not an accidental pass via a different one.
        var assignResponse = await org2Client.PostAsJsonAsync($"/api/workorders/{created!.Id}/assign", new { EmployeeId = 2 });

        Assert.Equal(HttpStatusCode.BadRequest, assignResponse.StatusCode);
    }

    private record WorkOrderDto(int Id, string Title, int OrganizationId, int Status, int? AssignedEmployeeId);
}
