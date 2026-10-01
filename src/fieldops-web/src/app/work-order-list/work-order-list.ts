import { Component, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { WorkOrder, WORK_ORDER_STATUS_LABELS } from '../work-order';
import { WorkOrderService } from '../work-order.service';
import { AuthService } from '../auth.service';

@Component({
  imports: [RouterLink],
  selector: 'app-work-order-list',
  styleUrl: './work-order-list.css',
  templateUrl: './work-order-list.html',
})
export class WorkOrderList implements OnInit {
  protected readonly workOrders = signal<WorkOrder[]>([]);
  protected readonly statusLabels = WORK_ORDER_STATUS_LABELS;

  // Day 95: "protected" (not "private") so work-order-list.html's template
  // can call authService.isAdmin() directly.
  constructor(
    private readonly workOrderService: WorkOrderService,
    protected readonly authService: AuthService,
  ) {}

  ngOnInit(): void {
    this.workOrderService.getAll().subscribe(workOrders => {
      this.workOrders.set(workOrders);
    });
  }
}
