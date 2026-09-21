namespace FieldOps.Api.Models;

public record WorkOrderStatusReport(int OrganizationId, int Open, int Assigned, int InProgress, int Completed);
