namespace FieldOps.Modules.WorkOrders;

// Day 40: only Open existed — no transitions at all. Day 41 adds the first
// real transition, Open -> Assigned. InProgress/Completed are still ahead,
// later in Week 9.
public enum WorkOrderStatus
{
    Open,
    Assigned
}
