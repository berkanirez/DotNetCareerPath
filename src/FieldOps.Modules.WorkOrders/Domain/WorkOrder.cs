namespace FieldOps.Modules.WorkOrders.Domain;

// OrganizationId is a plain int — NOT a reference to the Organizations
// module's Organization type, same reasoning as Employee.OrganizationId
// (Day 33, ADR 0002): no project reference to FieldOps.Modules.Organizations
// exists or is needed here.
internal class WorkOrder
{
    public int Id { get; set; }
    public string Title { get; set; }
    public int OrganizationId { get; set; }
    public WorkOrderStatus Status { get; set; }
    public int? AssignedEmployeeId { get; set; }

    // Day 46: a plain text note stands in for real file evidence (a photo,
    // a document) — no upload/storage infrastructure exists yet (that's
    // Week 10-11's territory). A deliberate demo simplification, not a
    // placeholder for something already half-built.
    public List<string> EvidenceNotes { get; } = new();

    // Day 47: CustomerId is a plain int — NOT a reference to the Customers
    // module's Customer type, same ADR 0002 reasoning as OrganizationId and
    // AssignedEmployeeId. Optional: not every work order needs a customer
    // on record today.
    public int? CustomerId { get; set; }
    public bool CustomerApproved { get; set; }

    public WorkOrder(string title, int organizationId, WorkOrderStatus status)
    {
        Title = title;
        OrganizationId = organizationId;
        Status = status;
    }
}
