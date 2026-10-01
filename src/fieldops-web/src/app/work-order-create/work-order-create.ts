import { Component } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { WorkOrderService } from '../work-order.service';

@Component({
  imports: [ReactiveFormsModule],
  selector: 'app-work-order-create',
  styleUrl: './work-order-create.css',
  templateUrl: './work-order-create.html',
})
export class WorkOrderCreate {
  // Day 92: mirrors FieldOps.Api's own CreateWorkOrderRequest validation
  // ([Required, StringLength(200)] on Title) on the client side too — this
  // doesn't replace the server's validation (the server is still the real
  // authority), it just gives the user feedback before a round trip.
  protected readonly form = new FormGroup({
    title: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(200)] }),
    customerId: new FormControl<number | null>(null),
  });

  constructor(
    private readonly workOrderService: WorkOrderService,
    private readonly router: Router,
  ) {}

  submit(): void {
    if (this.form.invalid) {
      return;
    }
    const { title, customerId } = this.form.getRawValue();
    this.workOrderService.create(title, customerId).subscribe(created => {
      this.router.navigate(['/work-orders', created.id]);
    });
  }
}
