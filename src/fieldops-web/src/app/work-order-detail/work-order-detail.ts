import { Component, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { WorkOrder, WORK_ORDER_STATUS_LABELS } from '../work-order';
import { WorkOrderService } from '../work-order.service';

@Component({
  imports: [RouterLink],
  selector: 'app-work-order-detail',
  styleUrl: './work-order-detail.css',
  templateUrl: './work-order-detail.html',
})
export class WorkOrderDetail implements OnInit {
  // Day 91 bugfix: same zoneless reasoning as WorkOrderList — signals, not
  // plain fields, are what Angular's zoneless change detection reacts to.
  protected readonly workOrder = signal<WorkOrder | undefined>(undefined);
  protected readonly notFound = signal(false);
  protected readonly statusLabels = WORK_ORDER_STATUS_LABELS;

  constructor(
    private readonly route: ActivatedRoute,
    private readonly workOrderService: WorkOrderService,
  ) {}

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    this.workOrderService.getById(id).subscribe(workOrder => {
      this.workOrder.set(workOrder);
      this.notFound.set(workOrder === undefined);
    });
  }
}
