import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';

import { SKIP_AUTH_REFRESH, SKIP_ERROR_TOAST } from '../api/http-context';
import { toProblem } from '../api/problem';
import { I18nService } from '../i18n/i18n.service';
import { ScopeStore } from '../state/scope.store';
import { ToastService } from '../ui/toast.service';
import { AuthService } from './auth.service';

/** Correlation id on every API call (01 section 7). */
export const correlationInterceptor: HttpInterceptorFn = (req, next) =>
  next(req.url.startsWith('/api/') ? req.clone({ setHeaders: { 'X-Correlation-Id': crypto.randomUUID().replaceAll('-', '') } }) : req);

/**
 * Bearer token, `X-Tenant-Id` inside a workspace (never on platform or auth calls), and a single refresh on 401
 * with the request retried once.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/api/') || req.context.get(SKIP_AUTH_REFRESH)) {
    return next(req);
  }
  const auth = inject(AuthService);
  const scope = inject(ScopeStore);

  const prepare = (request: HttpRequest<unknown>, token: string | null) => {
    const headers: Record<string, string> = {};
    if (token) {
      headers['Authorization'] = `Bearer ${token}`;
    }
    const workspace = scope.workspace();
    if (workspace && !request.url.startsWith('/api/v1/platform/') && !request.url.startsWith('/api/v1/auth/')) {
      headers['X-Tenant-Id'] = workspace.id;
    }
    return request.clone({ setHeaders: headers });
  };

  return next(prepare(req, auth.accessToken)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }
      return auth.refresh().pipe(
        switchMap((token) => {
          if (!token) {
            void auth.logout();
            return throwError(() => error);
          }
          return next(prepare(req, token));
        }),
      );
    }),
  );
};

/** Problem details become a translated toast (`error.{code}`), unless the caller handles errors itself. */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toast = inject(ToastService);
  const i18n = inject(I18nService);
  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status !== 401 && !req.context.get(SKIP_ERROR_TOAST)) {
        const problem = toProblem(error);
        toast.error(i18n.errorMessage(problem.code));
      }
      return throwError(() => error);
    }),
  );
};
