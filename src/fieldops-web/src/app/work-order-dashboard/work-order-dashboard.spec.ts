import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { WorkOrderDashboard } from './work-order-dashboard';
import { WorkOrderService } from '../work-order.service';

describe('WorkOrderDashboard', () => {
  function setUp(report: unknown) {
    TestBed.configureTestingModule({
      imports: [WorkOrderDashboard],
      providers: [{ provide: WorkOrderService, useValue: { getReport: () => of(report) } }],
    });
    const fixture: ComponentFixture<WorkOrderDashboard> = TestBed.createComponent(WorkOrderDashboard);
    fixture.detectChanges();
    return fixture;
  }

  it('shows a loading message before the report arrives', () => {
    TestBed.configureTestingModule({
      imports: [WorkOrderDashboard],
      providers: [{ provide: WorkOrderService, useValue: { getReport: () => of() } }],
    });
    const fixture = TestBed.createComponent(WorkOrderDashboard);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Loading...');
  });

  it('shows the real counts once the report arrives', () => {
    const fixture = setUp({ organizationId: 1, open: 3, assigned: 1, inProgress: 2, completed: 5 });
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Open: 3');
    expect(text).toContain('Assigned: 1');
    expect(text).toContain('In Progress: 2');
    expect(text).toContain('Completed: 5');
  });
});
