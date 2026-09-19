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

    // Day 42: ownership-based authorization — a work order created and
    // assigned by Org1's Admin (id=1) to Org1's Member (id=2); only that
    // Member, not even the Admin who assigned it, may start/complete it.
    private async Task<WorkOrderDto> CreateAndAssignWorkOrderAsync(HttpClient adminClient, string title, int assigneeEmployeeId)
    {
        var createResponse = await adminClient.PostAsJsonAsync("/api/workorders", new { Title = title });
        var created = await createResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        var assignResponse = await adminClient.PostAsJsonAsync($"/api/workorders/{created!.Id}/assign", new { EmployeeId = assigneeEmployeeId });
        return (await assignResponse.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }

    [Fact]
    public async Task Start_ByAssignedEmployee_TransitionsToInProgress()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var assigned = await CreateAndAssignWorkOrderAsync(org1AdminClient, $"Start-Me-{Guid.NewGuid():N}", assigneeEmployeeId: 2);

        var org1MemberClient = _factory.CreateClient();
        org1MemberClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1MemberClient.DefaultRequestHeaders.Add("X-Employee-Id", "2"); // the assignee

        var startResponse = await org1MemberClient.PostAsync($"/api/workorders/{assigned.Id}/start", null);
        var started = await startResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        Assert.Equal(2, started!.Status); // WorkOrderStatus.InProgress == 2
    }

    [Fact]
    public async Task Start_ByAdminWhoIsNotTheAssignee_ReturnsForbidden()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var assigned = await CreateAndAssignWorkOrderAsync(org1AdminClient, $"Admin-Cannot-Start-{Guid.NewGuid():N}", assigneeEmployeeId: 2);

        // The Admin assigned this work order but wasn't assigned it
        // themselves — ownership, not role, decides who may start it.
        var startResponse = await org1AdminClient.PostAsync($"/api/workorders/{assigned.Id}/start", null);

        Assert.Equal(HttpStatusCode.Forbidden, startResponse.StatusCode);
    }

    // Named ReturnsForbidden, not ReturnsBadRequest: the ownership check
    // (AssignedEmployeeId != actingEmployeeId) runs BEFORE the state check,
    // and a null AssignedEmployeeId can never equal a real employee id — so
    // an unassigned work order always fails ownership first, no matter who
    // asks. The "must be Assigned to start" state-invariant path is
    // therefore unreachable for a genuinely unassigned work order; it only
    // ever fires for one that's already past Assigned (e.g. already
    // InProgress), which Day 43+ may want a dedicated test for.
    [Fact]
    public async Task Start_OnUnassignedWorkOrder_ReturnsForbidden()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");

        var createResponse = await org1AdminClient.PostAsJsonAsync("/api/workorders", new { Title = $"Still-Open-{Guid.NewGuid():N}" });
        var created = await createResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        // Nobody is assigned yet, so AssignedEmployeeId is null — even the
        // Admin who created it isn't "the assignee" of a null assignment.
        var startResponse = await org1AdminClient.PostAsync($"/api/workorders/{created!.Id}/start", null);

        Assert.Equal(HttpStatusCode.Forbidden, startResponse.StatusCode);
    }

    [Fact]
    public async Task Complete_ByAssignedEmployee_TransitionsToCompleted()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var assigned = await CreateAndAssignWorkOrderAsync(org1AdminClient, $"Complete-Me-{Guid.NewGuid():N}", assigneeEmployeeId: 2);

        var org1MemberClient = _factory.CreateClient();
        org1MemberClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1MemberClient.DefaultRequestHeaders.Add("X-Employee-Id", "2");

        await org1MemberClient.PostAsync($"/api/workorders/{assigned.Id}/start", null);
        var completeResponse = await org1MemberClient.PostAsync($"/api/workorders/{assigned.Id}/complete", null);
        var completed = await completeResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        Assert.Equal(3, completed!.Status); // WorkOrderStatus.Completed == 3
    }

    [Fact]
    public async Task Complete_BeforeStart_ReturnsBadRequest()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var assigned = await CreateAndAssignWorkOrderAsync(org1AdminClient, $"Skip-Start-{Guid.NewGuid():N}", assigneeEmployeeId: 2);

        var org1MemberClient = _factory.CreateClient();
        org1MemberClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1MemberClient.DefaultRequestHeaders.Add("X-Employee-Id", "2");

        // Still Assigned, never started — completing straight from Assigned
        // skips a lifecycle step and must be rejected.
        var completeResponse = await org1MemberClient.PostAsync($"/api/workorders/{assigned.Id}/complete", null);

        Assert.Equal(HttpStatusCode.BadRequest, completeResponse.StatusCode);
    }

    // Day 43: creates a genuinely new Org1 employee via the Employees API,
    // so reassignment tests have a second real, valid target within Org1
    // without depending on more seed data than already exists.
    private static async Task<int> CreateOrg1EmployeeAsync(HttpClient org1AdminClient, string name)
    {
        var response = await org1AdminClient.PostAsJsonAsync("/api/employees", new { Name = name });
        var body = await response.Content.ReadFromJsonAsync<EmployeeDto>();
        return body!.Id;
    }

    [Fact]
    public async Task Reassign_WhileAssigned_ChangesAssigneeWithoutChangingStatus()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var assigned = await CreateAndAssignWorkOrderAsync(org1AdminClient, $"Reassign-Me-{Guid.NewGuid():N}", assigneeEmployeeId: 2);
        var newEmployeeId = await CreateOrg1EmployeeAsync(org1AdminClient, $"Cover-{Guid.NewGuid():N}");

        var reassignResponse = await org1AdminClient.PostAsJsonAsync($"/api/workorders/{assigned.Id}/reassign", new { EmployeeId = newEmployeeId });
        var reassigned = await reassignResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        Assert.Equal(HttpStatusCode.OK, reassignResponse.StatusCode);
        Assert.Equal(1, reassigned!.Status); // still WorkOrderStatus.Assigned
        Assert.Equal(newEmployeeId, reassigned.AssignedEmployeeId);
    }

    [Fact]
    public async Task Reassign_WhileInProgress_Succeeds()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var assigned = await CreateAndAssignWorkOrderAsync(org1AdminClient, $"Reassign-InProgress-{Guid.NewGuid():N}", assigneeEmployeeId: 2);

        var org1MemberClient = _factory.CreateClient();
        org1MemberClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1MemberClient.DefaultRequestHeaders.Add("X-Employee-Id", "2");
        await org1MemberClient.PostAsync($"/api/workorders/{assigned.Id}/start", null);

        var newEmployeeId = await CreateOrg1EmployeeAsync(org1AdminClient, $"Takeover-{Guid.NewGuid():N}");
        var reassignResponse = await org1AdminClient.PostAsJsonAsync($"/api/workorders/{assigned.Id}/reassign", new { EmployeeId = newEmployeeId });
        var reassigned = await reassignResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        Assert.Equal(HttpStatusCode.OK, reassignResponse.StatusCode);
        Assert.Equal(2, reassigned!.Status); // still WorkOrderStatus.InProgress
    }

    [Fact]
    public async Task Reassign_OnOpenWorkOrder_ReturnsBadRequest()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");

        var createResponse = await org1AdminClient.PostAsJsonAsync("/api/workorders", new { Title = $"Never-Assigned-{Guid.NewGuid():N}" });
        var created = await createResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        // Nothing to reassign — Reassign is for handing off already-assigned
        // work, not a substitute for the first Assign.
        var reassignResponse = await org1AdminClient.PostAsJsonAsync($"/api/workorders/{created!.Id}/reassign", new { EmployeeId = 2 });

        Assert.Equal(HttpStatusCode.BadRequest, reassignResponse.StatusCode);
    }

    [Fact]
    public async Task Reassign_OnCompletedWorkOrder_ReturnsBadRequest()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var assigned = await CreateAndAssignWorkOrderAsync(org1AdminClient, $"Finished-{Guid.NewGuid():N}", assigneeEmployeeId: 2);

        var org1MemberClient = _factory.CreateClient();
        org1MemberClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1MemberClient.DefaultRequestHeaders.Add("X-Employee-Id", "2");
        await org1MemberClient.PostAsync($"/api/workorders/{assigned.Id}/start", null);
        await org1MemberClient.PostAsync($"/api/workorders/{assigned.Id}/complete", null);

        var reassignResponse = await org1AdminClient.PostAsJsonAsync($"/api/workorders/{assigned.Id}/reassign", new { EmployeeId = 2 });

        Assert.Equal(HttpStatusCode.BadRequest, reassignResponse.StatusCode);
    }

    // Day 44: employee 2 (the seeded Org1 Member) is no longer a valid
    // "any Member is blocked" example, since it's also this test's assignee
    // and Day 44 made the assignee a legitimate actor for their OWN work
    // order. A genuinely unrelated Member — neither Admin nor the assignee —
    // is created fresh so this test still proves what it claims to.
    [Fact]
    public async Task Reassign_ByUnrelatedMember_ReturnsForbidden()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var assigned = await CreateAndAssignWorkOrderAsync(org1AdminClient, $"Unrelated-Cannot-Reassign-{Guid.NewGuid():N}", assigneeEmployeeId: 2);

        var unrelatedEmployeeId = await CreateOrg1EmployeeAsync(org1AdminClient, $"Bystander-{Guid.NewGuid():N}");
        var unrelatedClient = _factory.CreateClient();
        unrelatedClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        unrelatedClient.DefaultRequestHeaders.Add("X-Employee-Id", unrelatedEmployeeId.ToString());

        var reassignResponse = await unrelatedClient.PostAsJsonAsync($"/api/workorders/{assigned.Id}/reassign", new { EmployeeId = 2 });

        Assert.Equal(HttpStatusCode.Forbidden, reassignResponse.StatusCode);
    }

    // Day 44: the actual new capability — the current assignee (not an
    // Admin) hands their own work order off to someone else.
    [Fact]
    public async Task Reassign_ByCurrentAssignee_Succeeds()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var assigned = await CreateAndAssignWorkOrderAsync(org1AdminClient, $"Self-Handoff-{Guid.NewGuid():N}", assigneeEmployeeId: 2);
        var newEmployeeId = await CreateOrg1EmployeeAsync(org1AdminClient, $"Covering-{Guid.NewGuid():N}");

        var assigneeClient = _factory.CreateClient();
        assigneeClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        assigneeClient.DefaultRequestHeaders.Add("X-Employee-Id", "2"); // the assignee itself, not an Admin

        var reassignResponse = await assigneeClient.PostAsJsonAsync($"/api/workorders/{assigned.Id}/reassign", new { EmployeeId = newEmployeeId });
        var reassigned = await reassignResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        Assert.Equal(HttpStatusCode.OK, reassignResponse.StatusCode);
        Assert.Equal(newEmployeeId, reassigned!.AssignedEmployeeId);
    }

    [Fact]
    public async Task Reassign_ToEmployeeFromAnotherOrganization_ReturnsBadRequest()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var assigned = await CreateAndAssignWorkOrderAsync(org1AdminClient, $"Cross-Org-Reassign-{Guid.NewGuid():N}", assigneeEmployeeId: 2);

        var reassignResponse = await org1AdminClient.PostAsJsonAsync($"/api/workorders/{assigned.Id}/reassign", new { EmployeeId = 4 }); // seeded Org2 Member

        Assert.Equal(HttpStatusCode.BadRequest, reassignResponse.StatusCode);
    }

    // Day 44 independent-task fix: caught by Berkan reading the code, not
    // by any test — "reassigning" to the same employee already assigned
    // was a meaningless no-op that nothing rejected.
    [Fact]
    public async Task Reassign_ToSameEmployeeAlreadyAssigned_ReturnsBadRequest()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var assigned = await CreateAndAssignWorkOrderAsync(org1AdminClient, $"No-Op-Reassign-{Guid.NewGuid():N}", assigneeEmployeeId: 2);

        var reassignResponse = await org1AdminClient.PostAsJsonAsync($"/api/workorders/{assigned.Id}/reassign", new { EmployeeId = 2 });

        Assert.Equal(HttpStatusCode.BadRequest, reassignResponse.StatusCode);
    }

    [Fact]
    public async Task Unassign_ByAdmin_ReturnsToOpenWithNoAssignee()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var assigned = await CreateAndAssignWorkOrderAsync(org1AdminClient, $"Unassign-By-Admin-{Guid.NewGuid():N}", assigneeEmployeeId: 2);

        var unassignResponse = await org1AdminClient.PostAsync($"/api/workorders/{assigned.Id}/unassign", null);
        var unassigned = await unassignResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        Assert.Equal(HttpStatusCode.OK, unassignResponse.StatusCode);
        Assert.Equal(0, unassigned!.Status); // WorkOrderStatus.Open == 0
        Assert.Null(unassigned.AssignedEmployeeId);
    }

    [Fact]
    public async Task Unassign_ByCurrentAssignee_Succeeds()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var assigned = await CreateAndAssignWorkOrderAsync(org1AdminClient, $"Unassign-By-Self-{Guid.NewGuid():N}", assigneeEmployeeId: 2);

        var assigneeClient = _factory.CreateClient();
        assigneeClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        assigneeClient.DefaultRequestHeaders.Add("X-Employee-Id", "2"); // the assignee, not an Admin

        var unassignResponse = await assigneeClient.PostAsync($"/api/workorders/{assigned.Id}/unassign", null);

        Assert.Equal(HttpStatusCode.OK, unassignResponse.StatusCode);
    }

    [Fact]
    public async Task Unassign_ByUnrelatedMember_ReturnsForbidden()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var assigned = await CreateAndAssignWorkOrderAsync(org1AdminClient, $"Unassign-Unrelated-{Guid.NewGuid():N}", assigneeEmployeeId: 2);

        var unrelatedEmployeeId = await CreateOrg1EmployeeAsync(org1AdminClient, $"Bystander-{Guid.NewGuid():N}");
        var unrelatedClient = _factory.CreateClient();
        unrelatedClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        unrelatedClient.DefaultRequestHeaders.Add("X-Employee-Id", unrelatedEmployeeId.ToString());

        var unassignResponse = await unrelatedClient.PostAsync($"/api/workorders/{assigned.Id}/unassign", null);

        Assert.Equal(HttpStatusCode.Forbidden, unassignResponse.StatusCode);
    }

    [Fact]
    public async Task Unassign_OnOpenWorkOrder_ReturnsBadRequest()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");

        var createResponse = await org1AdminClient.PostAsJsonAsync("/api/workorders", new { Title = $"Nothing-To-Unassign-{Guid.NewGuid():N}" });
        var created = await createResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        var unassignResponse = await org1AdminClient.PostAsync($"/api/workorders/{created!.Id}/unassign", null);

        Assert.Equal(HttpStatusCode.BadRequest, unassignResponse.StatusCode);
    }

    [Fact]
    public async Task Unassign_OnCompletedWorkOrder_ReturnsBadRequest()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var assigned = await CreateAndAssignWorkOrderAsync(org1AdminClient, $"Already-Done-{Guid.NewGuid():N}", assigneeEmployeeId: 2);

        var org1MemberClient = _factory.CreateClient();
        org1MemberClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1MemberClient.DefaultRequestHeaders.Add("X-Employee-Id", "2");
        await org1MemberClient.PostAsync($"/api/workorders/{assigned.Id}/start", null);
        await org1MemberClient.PostAsync($"/api/workorders/{assigned.Id}/complete", null);

        var unassignResponse = await org1AdminClient.PostAsync($"/api/workorders/{assigned.Id}/unassign", null);

        Assert.Equal(HttpStatusCode.BadRequest, unassignResponse.StatusCode);
    }

    private static async Task<WorkOrderDto> CompleteFullLifecycleAsync(HttpClient adminClient, HttpClient assigneeClient, string title, int assigneeEmployeeId)
    {
        var createResponse = await adminClient.PostAsJsonAsync("/api/workorders", new { Title = title });
        var created = await createResponse.Content.ReadFromJsonAsync<WorkOrderDto>();
        await adminClient.PostAsJsonAsync($"/api/workorders/{created!.Id}/assign", new { EmployeeId = assigneeEmployeeId });
        await assigneeClient.PostAsync($"/api/workorders/{created.Id}/start", null);
        var completeResponse = await assigneeClient.PostAsync($"/api/workorders/{created.Id}/complete", null);
        return (await completeResponse.Content.ReadFromJsonAsync<WorkOrderDto>())!;
    }

    [Fact]
    public async Task Reopen_ByAdmin_ReturnsToInProgress()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var org1MemberClient = _factory.CreateClient();
        org1MemberClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1MemberClient.DefaultRequestHeaders.Add("X-Employee-Id", "2");
        var completed = await CompleteFullLifecycleAsync(org1AdminClient, org1MemberClient, $"Reopen-Me-{Guid.NewGuid():N}", assigneeEmployeeId: 2);

        var reopenResponse = await org1AdminClient.PostAsync($"/api/workorders/{completed.Id}/reopen", null);
        var reopened = await reopenResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        Assert.Equal(HttpStatusCode.OK, reopenResponse.StatusCode);
        Assert.Equal(2, reopened!.Status); // WorkOrderStatus.InProgress == 2
    }

    [Fact]
    public async Task Reopen_ByMember_ReturnsForbidden()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var org1MemberClient = _factory.CreateClient();
        org1MemberClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1MemberClient.DefaultRequestHeaders.Add("X-Employee-Id", "2");
        var completed = await CompleteFullLifecycleAsync(org1AdminClient, org1MemberClient, $"Member-Cannot-Reopen-{Guid.NewGuid():N}", assigneeEmployeeId: 2);

        // Even the assignee — who completed it themselves — cannot reopen;
        // this is deliberately Admin-only, unlike Reassign/Unassign.
        var reopenResponse = await org1MemberClient.PostAsync($"/api/workorders/{completed.Id}/reopen", null);

        Assert.Equal(HttpStatusCode.Forbidden, reopenResponse.StatusCode);
    }

    [Fact]
    public async Task Reopen_OnNonCompletedWorkOrder_ReturnsBadRequest()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");

        var createResponse = await org1AdminClient.PostAsJsonAsync("/api/workorders", new { Title = $"Not-Done-Yet-{Guid.NewGuid():N}" });
        var created = await createResponse.Content.ReadFromJsonAsync<WorkOrderDto>();

        var reopenResponse = await org1AdminClient.PostAsync($"/api/workorders/{created!.Id}/reopen", null);

        Assert.Equal(HttpStatusCode.BadRequest, reopenResponse.StatusCode);
    }

    // The exact exploit live-proven before this fix existed: an Org 2 Admin
    // reopening Org 1's completed work order just by naming its id, because
    // the first version of Reopen's authorization check never verified the
    // work order's own organization at all.
    [Fact]
    public async Task Reopen_ByAdminFromAnotherOrganization_ReturnsBadRequest()
    {
        var org1AdminClient = _factory.CreateClient();
        org1AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "1");
        var org1MemberClient = _factory.CreateClient();
        org1MemberClient.DefaultRequestHeaders.Add("X-Organization-Id", "1");
        org1MemberClient.DefaultRequestHeaders.Add("X-Employee-Id", "2");
        var completed = await CompleteFullLifecycleAsync(org1AdminClient, org1MemberClient, $"Org1-Private-{Guid.NewGuid():N}", assigneeEmployeeId: 2);

        var org2AdminClient = _factory.CreateClient();
        org2AdminClient.DefaultRequestHeaders.Add("X-Organization-Id", "2");
        org2AdminClient.DefaultRequestHeaders.Add("X-Employee-Id", "3");

        var reopenResponse = await org2AdminClient.PostAsync($"/api/workorders/{completed.Id}/reopen", null);

        Assert.Equal(HttpStatusCode.BadRequest, reopenResponse.StatusCode);
    }

    private record EmployeeDto(int Id, string Name, int OrganizationId, int Role);

    private record WorkOrderDto(int Id, string Title, int OrganizationId, int Status, int? AssignedEmployeeId);
}
