import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { AuthResult } from '../api/models';
import { ScopeStore } from '../state/scope.store';
import { ToastService } from '../ui/toast.service';
import { AuthService } from './auth.service';
import { areaGuard, authGuard, homeRedirectGuard, permissionGuard, workspaceGuard } from './guards';
import { authInterceptor, correlationInterceptor, errorInterceptor } from './interceptors';

function result(role: string, permissions: string[] = [], tenantId: string | null = 't-1'): AuthResult {
  return {
    accessToken: `access-${role}`,
    refreshToken: `refresh-${role}`,
    expiresAt: '2026-10-06T08:15:00Z',
    user: { id: 'u-1', fullName: 'Sample User', email: 'user@alpha.test', role, tenantId, tenantName: 'Alpha', permissions, language: 'en', locationIds: [] },
  };
}

describe('auth', () => {
  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([correlationInterceptor, errorInterceptor, authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => http.verify());

  async function signIn(role = 'Administrator', permissions: string[] = ['users.manage'], tenantId: string | null = 't-1'): Promise<void> {
    const done = auth.login('user@alpha.test', 'pw');
    http.expectOne('/api/v1/auth/login').flush(result(role, permissions, tenantId));
    await done;
  }

  describe('AuthService', () => {
    it('signs in, keeps the access token in memory and the refresh token in sessionStorage', async () => {
      await signIn();
      expect(auth.isSignedIn()).toBe(true);
      expect(auth.accessToken).toBe('access-Administrator');
      expect(sessionStorage.getItem('mc.refresh')).toBe('refresh-Administrator');
      expect(localStorage.getItem('mc.refresh')).toBeNull();
      expect(auth.hasPermission('users.manage')).toBe(true);
      expect(auth.home()).toBe('/app/overview');
    });

    it('knows platform users', async () => {
      await signIn('PlatformAdmin', [], null);
      expect(auth.isPlatform()).toBe(true);
      expect(auth.home()).toBe('/admin/dashboard');
    });

    it('restores the session with the stored refresh token', async () => {
      sessionStorage.setItem('mc.refresh', 'stored');
      const restored = auth.restore();
      const req = http.expectOne('/api/v1/auth/refresh');
      expect(req.request.body).toEqual({ refreshToken: 'stored' });
      req.flush(result('Technician'));
      expect(await restored).toBe(true);
      expect(auth.user()?.role).toBe('Technician');
    });

    it('fails to restore without a stored token', async () => {
      expect(await auth.restore()).toBe(false);
    });

    it('clears the session when the refresh fails', async () => {
      sessionStorage.setItem('mc.refresh', 'stale');
      const restored = auth.restore();
      http.expectOne('/api/v1/auth/refresh').flush({ code: 'AUTH_REFRESH_INVALID' }, { status: 401, statusText: 'Unauthorized' });
      expect(await restored).toBe(false);
      expect(sessionStorage.getItem('mc.refresh')).toBeNull();
    });

    it('signs out on the server and locally', async () => {
      await signIn();
      const router = TestBed.inject(Router);
      const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
      const done = auth.logout();
      http.expectOne('/api/v1/auth/logout').flush(null);
      await done;
      expect(auth.isSignedIn()).toBe(false);
      expect(navigate).toHaveBeenCalledWith('/login');
    });

    it('saves the language preference', async () => {
      await signIn();
      const done = auth.setLanguage('ar');
      const req = await vi.waitFor(() => http.expectOne('/api/v1/auth/me/language'));
      expect(req.request.body).toEqual({ language: 'ar' });
      req.flush(null);
      await done;
      expect(auth.user()?.language).toBe('ar');
      expect(document.documentElement.dir).toBe('rtl');
      const back = auth.setLanguage('en');
      (await vi.waitFor(() => http.expectOne('/api/v1/auth/me/language'))).flush(null);
      await back;
    });
  });

  describe('interceptors', () => {
    it('adds the bearer token and a correlation id to API calls', async () => {
      await signIn();
      void firstValueFrom(TestBed.inject(HttpClient).get('/api/v1/locations'));
      const req = http.expectOne('/api/v1/locations');
      expect(req.request.headers.get('Authorization')).toBe('Bearer access-Administrator');
      expect(req.request.headers.get('X-Correlation-Id')).toMatch(/^[0-9a-f]{32}$/);
      expect(req.request.headers.has('X-Tenant-Id')).toBe(false);
      req.flush({});
    });

    it('adds X-Tenant-Id inside a workspace, but never on platform or auth calls', async () => {
      await signIn('PlatformAdmin', [], null);
      TestBed.inject(ScopeStore).enterWorkspace({ id: 'tenant-a', name: 'Alpha' });
      const client = TestBed.inject(HttpClient);
      void firstValueFrom(client.get('/api/v1/locations'));
      void firstValueFrom(client.get('/api/v1/platform/tenants'));
      void firstValueFrom(client.get('/api/v1/auth/me'));
      expect(http.expectOne('/api/v1/locations').request.headers.get('X-Tenant-Id')).toBe('tenant-a');
      expect(http.expectOne('/api/v1/platform/tenants').request.headers.has('X-Tenant-Id')).toBe(false);
      expect(http.expectOne('/api/v1/auth/me').request.headers.has('X-Tenant-Id')).toBe(false);
      http.match(() => true).forEach((r) => r.flush({}));
    });

    it('refreshes once on 401 for queued requests and retries them', async () => {
      await signIn();
      const client = TestBed.inject(HttpClient);
      const first = firstValueFrom(client.get<{ n: number }>('/api/v1/a'));
      const second = firstValueFrom(client.get<{ n: number }>('/api/v1/b'));
      http.expectOne('/api/v1/a').flush({}, { status: 401, statusText: 'Unauthorized' });
      http.expectOne('/api/v1/b').flush({}, { status: 401, statusText: 'Unauthorized' });
      const refreshes = http.match('/api/v1/auth/refresh');
      expect(refreshes.length).toBe(1);
      refreshes[0].flush({ ...result('Administrator', ['users.manage']), accessToken: 'access-2' });
      const retryA = http.expectOne('/api/v1/a');
      const retryB = http.expectOne('/api/v1/b');
      expect(retryA.request.headers.get('Authorization')).toBe('Bearer access-2');
      retryA.flush({ n: 1 });
      retryB.flush({ n: 2 });
      expect((await first).n).toBe(1);
      expect((await second).n).toBe(2);
    });

    it('signs out when the refresh fails', async () => {
      await signIn();
      const router = TestBed.inject(Router);
      vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
      const call = firstValueFrom(TestBed.inject(HttpClient).get('/api/v1/a')).catch((e: unknown) => e);
      http.expectOne('/api/v1/a').flush({}, { status: 401, statusText: 'Unauthorized' });
      http.expectOne('/api/v1/auth/refresh').flush({ code: 'AUTH_REFRESH_INVALID' }, { status: 401, statusText: 'Unauthorized' });
      await call;
      http.match('/api/v1/auth/logout').forEach((r) => r.flush(null));
      expect(auth.isSignedIn()).toBe(false);
    });

    it('turns problem details into translated toasts', async () => {
      const toasts = TestBed.inject(ToastService);
      const call = firstValueFrom(TestBed.inject(HttpClient).get('/api/v1/x')).catch(() => undefined);
      http.expectOne('/api/v1/x').flush({ code: 'LOCATION_NOT_EMPTY' }, { status: 409, statusText: 'Conflict' });
      await call;
      expect(toasts.toasts()[0].message).toBe('The location still has devices.');
    });

    it('uses a generic message for unknown codes and skips non-API URLs', async () => {
      const toasts = TestBed.inject(ToastService);
      const call = firstValueFrom(TestBed.inject(HttpClient).get('/api/v1/y')).catch(() => undefined);
      http.expectOne('/api/v1/y').flush('oops', { status: 500, statusText: 'Server Error' });
      await call;
      expect(toasts.toasts()[0].message).toBe('An unexpected error occurred.');
      void firstValueFrom(TestBed.inject(HttpClient).get('/assets/x.json'));
      expect(http.expectOne('/assets/x.json').request.headers.has('X-Correlation-Id')).toBe(false);
      http.match(() => true).forEach((r) => r.flush({}));
    });
  });

  describe('guards', () => {
    const route = (data: Record<string, unknown> = {}, params: Record<string, string> = {}) =>
      ({ data, paramMap: { get: (k: string) => params[k] ?? null } }) as unknown as ActivatedRouteSnapshot;
    const state = { url: '/app/users' } as RouterStateSnapshot;
    const run = <T>(fn: () => T) => TestBed.runInInjectionContext(fn);

    it('authGuard sends anonymous users to /login with the return URL', async () => {
      const outcome = (await run(() => authGuard(route(), state))) as UrlTree;
      expect(outcome.toString()).toBe('/login?returnUrl=%2Fapp%2Fusers');
    });

    it('authGuard lets signed-in users through', async () => {
      await signIn();
      expect(await run(() => authGuard(route(), state))).toBe(true);
    });

    it('areaGuard keeps tenant users out of /admin and platform users out of /app', async () => {
      await signIn('ReportViewer');
      expect((run(() => areaGuard('platform')(route(), state)) as UrlTree).toString()).toBe('/app/overview');
      expect(run(() => areaGuard('tenant')(route(), state))).toBe(true);
    });

    it('permissionGuard blocks a route whose permission is missing', async () => {
      await signIn('ReportViewer', ['reports.read']);
      expect((run(() => permissionGuard(route({ permission: 'users.manage' }), state)) as UrlTree).toString()).toBe('/app/overview');
      expect(run(() => permissionGuard(route({ permission: 'reports.read' }), state))).toBe(true);
      expect(run(() => permissionGuard(route(), state))).toBe(true);
    });

    it('workspaceGuard requires the workspace to be opened first', () => {
      const scope = TestBed.inject(ScopeStore);
      expect((run(() => workspaceGuard(route({}, { tenantId: 'a' }), state)) as UrlTree).toString()).toBe('/admin/customers');
      scope.enterWorkspace({ id: 'a', name: 'Alpha' });
      expect(run(() => workspaceGuard(route({}, { tenantId: 'a' }), state))).toBe(true);
    });

    it('homeRedirectGuard sends users home or to login', async () => {
      expect(((await run(() => homeRedirectGuard(route(), state))) as UrlTree).toString()).toBe('/login');
      await signIn('PlatformSupport', [], null);
      expect(((await run(() => homeRedirectGuard(route(), state))) as UrlTree).toString()).toBe('/admin/dashboard');
    });
  });
});
