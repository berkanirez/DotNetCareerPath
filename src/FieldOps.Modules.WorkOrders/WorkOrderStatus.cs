namespace FieldOps.Modules.WorkOrders;

// Only Open exists today — this is the first step of the lifecycle Week 9
// will build out (Assigned, InProgress, Completed, ...). No transitions
// between statuses exist yet; every work order is created Open and stays
// Open, on purpose, until a later day introduces real transition rules.
public enum WorkOrderStatus
{
    Open
}
