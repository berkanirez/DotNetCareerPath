using FieldOps.Modules.WorkOrders;

namespace FieldOps.Api.Application;

// Same Success/Failure factory pattern as EmployeeCreationResult (Day 34) —
// a plain outcome with no ASP.NET Core dependency, so WorkOrderAssignmentService
// stays fully testable without any HTTP pipeline involved.
public class WorkOrderAssignmentResult
{
    public bool Succeeded { get; }
    public WorkOrderSummary? WorkOrder { get; }
    public string? Error { get; }

    private WorkOrderAssignmentResult(bool succeeded, WorkOrderSummary? workOrder, string? error)
    {
        Succeeded = succeeded;
        WorkOrder = workOrder;
        Error = error;
    }

    public static WorkOrderAssignmentResult Success(WorkOrderSummary workOrder) =>
        new(succeeded: true, workOrder, error: null);

    public static WorkOrderAssignmentResult Failure(string error) =>
        new(succeeded: false, workOrder: null, error);
}
