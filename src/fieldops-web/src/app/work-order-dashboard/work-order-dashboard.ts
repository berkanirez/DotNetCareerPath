import { Component, OnInit, signal } from '@angular/core';
import { WorkOrderService } from '../work-order.service';
import { WorkOrderStatusReport } from '../work-order-status-report';

@Component({
  imports: [],
  selector: 'app-work-order-dashboard',
  styleUrl: './work-order-dashboard.css',
  templateUrl: './work-order-dashboard.html',
})
export class WorkOrderDashboard implements OnInit {
  protected readonly report = signal<WorkOrderStatusReport | null>(null);

  constructor(private readonly workOrderService: WorkOrderService) {}

  ngOnInit(): void {
    this.workOrderService.getReport().subscribe(report => {
      this.report.set(report);
    });
  }
}
