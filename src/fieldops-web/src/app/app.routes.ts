import { Routes } from '@angular/router';
import { WorkOrderList } from './work-order-list/work-order-list';
import { WorkOrderDetail } from './work-order-detail/work-order-detail';
import { WorkOrderCreate } from './work-order-create/work-order-create';
import { Login } from './login/login';
import { WorkOrderDashboard } from './work-order-dashboard/work-order-dashboard';

export const routes: Routes = [
  { path: '', component: WorkOrderList },
  { path: 'login', component: Login },
  { path: 'dashboard', component: WorkOrderDashboard },
  // Day 92: 'work-orders/new' MUST come before 'work-orders/:id' — the
  // Router matches routes in array order, and ':id' would otherwise greedily
  // match the literal word "new" as if it were an id.
  { path: 'work-orders/new', component: WorkOrderCreate },
  { path: 'work-orders/:id', component: WorkOrderDetail },
];
