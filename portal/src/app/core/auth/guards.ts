import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { ScopeStore } from '../state/scope.store';
import { AuthService } from './auth.service';

/** Signed in (restoring the session after a reload); otherwise to /login. */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (await auth.restore()) {
    return true;
  }
  return router.createUrlTree(['/login'], { queryParams: state.url && state.url !== '/' ? { returnUrl: state.url } : {} });
};

/** Platform area for platform roles only, customer area for tenant roles only. */
export function areaGuard(area: 'platform' | 'tenant'): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    const allowed = area === 'platform' ? auth.isPlatform() : !auth.isPlatform();
    return allowed ? true : inject(Router).createUrlTree([auth.home()]);
  };
}

/** Route data `permission`: a hidden menu item's route is blocked too (07 section 3). */
export const permissionGuard: CanActivateFn = (route) => {
  const auth = inject(AuthService);
  const permission = route.data['permission'] as string | undefined;
  if (!permission || auth.hasPermission(permission)) {
    return true;
  }
  return inject(Router).createUrlTree([auth.home()]);
};

/** A workspace route needs the workspace opened (with a reason) through the Customers screen. */
export const workspaceGuard: CanActivateFn = (route) => {
  const scope = inject(ScopeStore);
  const tenantId = route.paramMap.get('tenantId');
  return scope.workspace()?.id === tenantId ? true : inject(Router).createUrlTree(['/admin/customers']);
};

/** Sends `/` to the user's home. */
export const homeRedirectGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return (await auth.restore()) ? router.createUrlTree([auth.home()]) : router.createUrlTree(['/login']);
};
