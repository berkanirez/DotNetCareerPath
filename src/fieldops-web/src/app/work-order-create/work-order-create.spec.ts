import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { WorkOrderCreate } from './work-order-create';
import { WorkOrderService } from '../work-order.service';

describe('WorkOrderCreate', () => {
  let component: WorkOrderCreate;
  let fixture: ComponentFixture<WorkOrderCreate>;
  let createSpy: ReturnType<typeof vi.fn>;
  let navigateSpy: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    createSpy = vi.fn().mockReturnValue(of({ id: 42, title: 'New one', status: 0 }));
    navigateSpy = vi.fn();

    TestBed.configureTestingModule({
      imports: [WorkOrderCreate],
      providers: [
        { provide: WorkOrderService, useValue: { create: createSpy } },
        { provide: Router, useValue: { navigate: navigateSpy } },
      ],
    });

    fixture = TestBed.createComponent(WorkOrderCreate);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('starts invalid with an empty title, so the submit button is disabled', () => {
    expect(component['form'].invalid).toBe(true);
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('button[type="submit"]');
    expect(button.disabled).toBe(true);
  });

  it('does not call the service when the form is invalid', () => {
    component.submit();
    expect(createSpy).not.toHaveBeenCalled();
  });

  it('calls the service and navigates to the new work order on valid submit', () => {
    component['form'].controls.title.setValue('A real title');
    component.submit();

    expect(createSpy).toHaveBeenCalledWith('A real title', null);
    expect(navigateSpy).toHaveBeenCalledWith(['/work-orders', 42]);
  });
});
