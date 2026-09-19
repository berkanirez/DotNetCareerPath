using FieldOps.Modules.WorkOrders;

namespace FieldOps.Api.Models;

public record WorkOrderDto(int Id, string Title, int OrganizationId, WorkOrderStatus Status);
