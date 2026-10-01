import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { WorkOrderDetail } from './work-order-detail';
import { WorkOrderService } from '../work-order.service';

describe('WorkOrderDetail', () => {
  let component: WorkOrderDetail;
  let fixture: ComponentFixture<WorkOrderDetail>;

  function setUp(stubWorkOrder: unknown) {
    TestBed.configureTestingModule({
      imports: [WorkOrderDetail],
      providers: [
        provideRouter([]),
        { provide: WorkOrderService, useValue: { getById: () => of(stubWorkOrder) } },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: '28' }) } },
        },
      ],
    });
    fixture = TestBed.createComponent(WorkOrderDetail);
    component = fixture.componentInstance;
  }

  it('should create', () => {
    setUp({ id: 28, title: 'Test', status: 0 });
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('shows the work order returned by the service', () => {
    setUp({ id: 28, title: 'Replace pump', status: 2 });
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Replace pump');
    expect(text).toContain('InProgress');
  });

  it('shows a not-found message when the service returns nothing', () => {
    setUp(undefined);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No work order found');
  });
});
