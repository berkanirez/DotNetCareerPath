import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';

// Day 94: a functional interceptor — modern Angular's way of writing one
// (vs. the older class-based HttpInterceptor interface). Runs for EVERY
// outgoing HttpClient request in the app, so WorkOrderService never needs
// to know about tokens at all.
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const token = authService.getToken();

  if (!token) {
    return next(req);
  }

  // req is immutable — .clone() is the only way to add a header; it returns
  // a NEW request object rather than mutating the original.
  const authorizedRequest = req.clone({
    setHeaders: { Authorization: `Bearer ${token}` },
  });
  return next(authorizedRequest);
};
