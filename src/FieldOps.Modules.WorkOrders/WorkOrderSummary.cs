namespace FieldOps.Modules.WorkOrders;

public record WorkOrderSummary(int Id, string Title, int OrganizationId, WorkOrderStatus Status, int? AssignedEmployeeId, IReadOnlyList<string> EvidenceNotes);
