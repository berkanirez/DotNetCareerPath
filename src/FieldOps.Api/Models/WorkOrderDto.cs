using FieldOps.Modules.WorkOrders;

namespace FieldOps.Api.Models;

public record WorkOrderDto(int Id, string Title, int OrganizationId, WorkOrderStatus Status, int? AssignedEmployeeId, IReadOnlyList<string> EvidenceNotes, int? CustomerId, bool CustomerApproved);
