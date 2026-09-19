namespace FieldOps.Modules.WorkOrders;

// Day 40: only Open. Day 41: Open -> Assigned. Day 42: Assigned -> InProgress
// -> Completed, both driven by the assigned employee, not just any Admin.
public enum WorkOrderStatus
{
    Open,
    Assigned,
    InProgress,
    Completed
}
