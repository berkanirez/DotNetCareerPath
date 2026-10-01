export interface WorkOrderStatusReport {
  organizationId: number;
  open: number;
  assigned: number;
  inProgress: number;
  completed: number;
}
