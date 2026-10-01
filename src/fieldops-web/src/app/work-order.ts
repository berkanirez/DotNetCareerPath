export interface WorkOrder {
  id: number;
  title: string;
  // Day 90: FieldOps.Api serializes WorkOrderStatus (a C# enum) as its
  // underlying number (0=Open, 1=Assigned, 2=InProgress, 3=Completed) —
  // System.Text.Json's default, no JsonStringEnumConverter configured.
  status: number;
}

export const WORK_ORDER_STATUS_LABELS: readonly string[] = ['Open', 'Assigned', 'InProgress', 'Completed'];
