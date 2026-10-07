import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';

import { AuthResult } from '../core/api/models';
import { AuthService } from '../core/auth/auth.service';
import { ScopeStore } from '../core/state/scope.store';
import { render, text } from '../testing/render';
import { AcceptInvitationPage } from './auth/accept-invitation.page';
import { LoginPage } from './auth/login.page';
import { LocationsPage } from './locations/locations.page';
import { CustomersPage } from './platform/customers.page';
import { ComingSoonPage } from './shared/coming-soon.page';
import { PlatformUsersPage } from './users/platform-users.page';
import { UsersPage } from './users/users.page';

const settle = async () => {
  for (let i = 0; i < 5; i++) {
    await Promise.resolve();
  }
};

describe('feature screens', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: '**', children: [] }]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  async function signIn(role: string, permissions: string[], tenantId: string | null): Promise<void> {
    const auth = TestBed.inject(AuthService);
    const done = auth.login('u@x.test', 'pw');
    const body: AuthResult = {
      accessToken: 'a', refreshToken: 'r', expiresAt: '',
      user: { id: '1', fullName: 'Admin User', email: 'u@x.test', role, tenantId, tenantName: null, permissions, language: 'en', locationIds: [] },
    };
    http.expectOne('/api/v1/auth/login').flush(body);
    await done;
  }

  describe('LoginPage', () => {
    it('signs in and goes home', async () => {
      const router = TestBed.inject(Router);
      const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
      const { fixture } = await render(LoginPage);
      const page = fixture.componentInstance as unknown as { email: string; password: string; submit(): Promise<void> };
      page.email = 'admin@monitor.local';
      page.password = 'Admin@12345';
      const submitted = page.submit();
      const body: AuthResult = { accessToken: 'a', refreshToken: 'r', expiresAt: '', user: { id: '1', fullName: 'A', email: 'a', role: 'PlatformAdmin', tenantId: null, tenantName: null, permissions: [], language: 'en', locationIds: [] } };
      http.expectOne('/api/v1/auth/login').flush(body);
      await submitted;
      expect(navigate).toHaveBeenCalledWith('/admin/dashboard');
    });

    it('shows the translated error for a suspended customer', async () => {
      const { fixture, host } = await render(LoginPage);
      const page = fixture.componentInstance as unknown as { email: string; password: string; submit(): Promise<void> };
      page.email = 'admin@oasis.test';
      page.password = 'Demo@12345';
      const submitted = page.submit();
      http.expectOne('/api/v1/auth/login').flush({ code: 'AUTH_TENANT_SUSPENDED' }, { status: 403, statusText: 'Forbidden' });
      await submitted;
      fixture.detectChanges();
      expect(text(host, '[data-testid="login-error"]')).toBe('This customer account is suspended.');
    });

    it('shows minutes left when locked and requires both fields', async () => {
      const { fixture, host } = await render(LoginPage);
      const page = fixture.componentInstance as unknown as { email: string; password: string; submit(): Promise<void> };
      await page.submit();
      fixture.detectChanges();
      expect(text(host, '[data-testid="login-error"]')).toBe('Enter your e-mail and password.');
      page.email = 'x@y.test';
      page.password = 'pw';
      const submitted = page.submit();
      http.expectOne('/api/v1/auth/login').flush({ code: 'AUTH_LOCKED', retryAfterSeconds: 840 }, { status: 423, statusText: 'Locked' });
      await submitted;
      fixture.detectChanges();
      expect(text(host, '[data-testid="login-error"]')).toBe('Too many failed sign-ins. Try again in 14 minutes.');
    });
  });

  describe('AcceptInvitationPage', () => {
    it('checks the confirmation and sets the password', async () => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => 'tok' } } } }],
      });
      http = TestBed.inject(HttpTestingController);
      const { fixture, host } = await render(AcceptInvitationPage);
      const page = fixture.componentInstance as unknown as { password: string; confirm: string; submit(): Promise<void> };
      page.password = 'Chosen-Pass#2026';
      page.confirm = 'other';
      await page.submit();
      fixture.detectChanges();
      expect(host.textContent).toContain('The passwords do not match.');
      page.confirm = 'Chosen-Pass#2026';
      const submitted = page.submit();
      const req = http.expectOne('/api/v1/auth/invitations/accept');
      expect(req.request.body).toEqual({ token: 'tok', password: 'Chosen-Pass#2026' });
      req.flush(null);
      await submitted;
      fixture.detectChanges();
      expect(host.textContent).toContain('Your password is set.');
    });
  });

  describe('CustomersPage', () => {
    const page = { items: [{ id: 'acme', name: 'Acme Corporation', code: 'ACME', status: 'Active', city: 'Cairo', country: 'Egypt', customerSince: '2024-01-06', locations: 3, planCode: null, planName: null, devices: null, healthy: null, warning: null, critical: null, healthScore: null, licensesUsed: null, licenseLimit: null, nextRenewal: null }], total: 1, page: 1, pageSize: 24 };

    async function open() {
      await signIn('PlatformAdmin', ['platform.tenants.read', 'platform.tenants.manage', 'platform.workspace.open'], null);
      const rendered = await render(CustomersPage);
      http.expectOne('/api/v1/platform/tenants/summary').flush({ total: 48, active: 46, expiringSoon: 0, suspended: 2 });
      http.expectOne((r) => r.url === '/api/v1/platform/tenants').flush(page);
      await settle();
      rendered.fixture.detectChanges();
      return rendered;
    }

    it('shows tiles and customer cards', async () => {
      const { host } = await open();
      expect(Array.from(host.querySelectorAll('[data-testid="kpi-value"]')).map((e) => e.textContent?.trim())).toEqual(['48', '46', '0', '2']);
      expect(host.querySelectorAll('[data-testid="customer-card"]').length).toBe(1);
      expect(text(host, '[data-testid="customer-card"] h2')).toBe('Acme Corporation');
    });

    it('opens a workspace with a reason', async () => {
      const { fixture, host } = await open();
      const router = TestBed.inject(Router);
      const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
      (host.querySelector('[data-testid="open-workspace"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      const confirmed = (fixture.componentInstance as unknown as { confirm(r: string): Promise<void> }).confirm('Customer called about alerts');
      const req = http.expectOne('/api/v1/platform/tenants/acme/workspace-sessions');
      expect(req.request.body).toEqual({ reason: 'Customer called about alerts' });
      req.flush(null);
      await confirmed;
      expect(TestBed.inject(ScopeStore).workspace()).toEqual({ id: 'acme', name: 'Acme Corporation' });
      expect(navigate).toHaveBeenCalledWith(['/admin/customers', 'acme', 'overview']);
    });

    it('suspends with a reason and reloads', async () => {
      const { fixture } = await open();
      const component = fixture.componentInstance as unknown as { ask(k: string, t: unknown): void; confirm(r: string): Promise<void> };
      component.ask('suspend', page.items[0]);
      const confirmed = component.confirm('Unpaid invoice for months');
      http.expectOne('/api/v1/platform/tenants/acme/suspend').flush({});
      await settle();
      http.expectOne('/api/v1/platform/tenants/summary').flush({ total: 48, active: 45, expiringSoon: 0, suspended: 3 });
      http.expectOne((r) => r.url === '/api/v1/platform/tenants').flush(page);
      await confirmed;
    });

    it('filters by status and resets to page one', async () => {
      const { fixture } = await open();
      (fixture.componentInstance as unknown as { setFilter(f: () => void): void; status: { set(v: string): void } }).setFilter(() =>
        (fixture.componentInstance as unknown as { status: { set(v: string): void } }).status.set('Suspended'));
      http.expectOne('/api/v1/platform/tenants/summary').flush({ total: 48, active: 46, expiringSoon: 0, suspended: 2 });
      const req = http.expectOne((r) => r.url === '/api/v1/platform/tenants');
      expect(req.request.params.get('subscriptionStatus')).toBe('Suspended');
      expect(req.request.params.get('page')).toBe('1');
      req.flush(page);
    });

    it('shows the error state when loading fails', async () => {
      await signIn('PlatformAdmin', ['platform.tenants.read'], null);
      const { fixture, host } = await render(CustomersPage);
      http.expectOne('/api/v1/platform/tenants/summary').flush({}, { status: 500, statusText: 'Error' });
      http.match((r) => r.url === '/api/v1/platform/tenants').forEach((r) => r.flush({}, { status: 500, statusText: 'Error' }));
      await settle();
      fixture.detectChanges();
      expect(host.querySelector('mc-error-state')).not.toBeNull();
    });
  });

  describe('UsersPage', () => {
    const users = { items: [{ id: 'u1', fullName: 'Alpha Admin', email: 'admin@alpha.test', role: 'Administrator', roleName: 'Administrator', permissionSummary: 'Full Access', status: 'Active', lastLoginAt: null, locationIds: [], version: 'AAA=' }], total: 1, page: 1, pageSize: 20 };

    async function open() {
      await signIn('Administrator', ['users.manage', 'locations.read'], 't');
      const rendered = await render(UsersPage);
      http.match((r) => r.url === '/api/v1/users').forEach((r) => r.flush(users));
      http.match((r) => r.url === '/api/v1/locations').forEach((r) => r.flush({ items: [], total: 0, page: 1, pageSize: 200 }));
      await settle();
      rendered.fixture.detectChanges();
      return rendered;
    }

    it('lists users with role and permission summary', async () => {
      const { host } = await open();
      expect(host.querySelectorAll('[data-testid="table-row"]').length).toBe(1);
      expect(host.textContent).toContain('Full Access');
      expect(host.querySelector('[data-testid="tab-all"]')?.textContent).toContain('(1)');
    });

    it('invites a user', async () => {
      const { fixture } = await open();
      (fixture.nativeElement.querySelector('[data-testid="add-user"]') as HTMLButtonElement).click();
      const component = fixture.componentInstance as unknown as { form: () => { fullName: string; email: string } | null; save(): Promise<void> };
      const form = component.form()!;
      form.fullName = 'New Person';
      form.email = 'new@alpha.test';
      const saved = component.save();
      const req = http.expectOne('/api/v1/users');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ fullName: 'New Person', email: 'new@alpha.test', role: 'Technician', locationIds: [] });
      req.flush(users.items[0]);
      await settle();
      http.match((r) => r.url === '/api/v1/users').forEach((r) => r.flush(users));
      await saved;
      expect(component.form()).toBeNull();
    });

    it('deactivates a user from the row', async () => {
      const { host } = await open();
      (host.querySelector('[data-testid="deactivate"]') as HTMLButtonElement).click();
      http.expectOne('/api/v1/users/u1/deactivate').flush(users.items[0]);
      await settle();
      http.match((r) => r.url === '/api/v1/users').forEach((r) => r.flush(users));
    });
  });

  describe('PlatformUsersPage', () => {
    it('lists and creates platform users', async () => {
      await signIn('PlatformAdmin', ['platform.users.manage'], null);
      const { fixture, host } = await render(PlatformUsersPage);
      http.expectOne((r) => r.url === '/api/v1/platform/users').flush({ items: [], total: 0, page: 1, pageSize: 20 });
      await settle();
      fixture.detectChanges();
      expect(host.textContent).toContain('No users found.');
      const component = fixture.componentInstance as unknown as { fullName: string; email: string; password: string; save(): Promise<void> };
      component.fullName = 'Support Two';
      component.email = 'support2@monitor.local';
      component.password = 'Support-Pass#2026';
      const saved = component.save();
      http.expectOne('/api/v1/platform/users').flush({ code: 'VALIDATION_FAILED', errors: { password: ['Too weak.'] } }, { status: 400, statusText: 'Bad Request' });
      await saved;
      fixture.detectChanges();
    });
  });

  describe('LocationsPage', () => {
    it('lists locations and creates one', async () => {
      await signIn('Administrator', ['locations.read', 'locations.manage'], 't');
      const { fixture, host } = await render(LocationsPage);
      http.expectOne((r) => r.url === '/api/v1/locations').flush({ items: [{ id: 'l1', name: 'Cairo HQ', code: 'CAIRO-HQ', city: 'Cairo', country: 'Egypt', isDefault: false, status: 'Active', devices: 0, online: 0, warning: 0, critical: 0, healthScore: null }], total: 1, page: 1, pageSize: 24 });
      await settle();
      fixture.detectChanges();
      expect(text(host, '[data-testid="location-card"] h2')).toBe('Cairo HQ');
      expect(text(host, '.place')).toBe('Cairo, Egypt');
      (host.querySelector('[data-testid="add-location"]') as HTMLButtonElement).click();
      const component = fixture.componentInstance as unknown as { form: () => { name: string; code: string } | null; save(): Promise<void> };
      component.form()!.name = 'Giza';
      component.form()!.code = 'GIZA';
      const saved = component.save();
      const req = http.expectOne('/api/v1/locations');
      expect(req.request.body.name).toBe('Giza');
      req.flush({ code: 'LOCATION_CODE_TAKEN' }, { status: 409, statusText: 'Conflict' });
      await saved;
      fixture.detectChanges();
      expect(host.textContent).toContain('Another location already uses this code.');
    });
  });

  describe('ComingSoonPage', () => {
    it('names the milestone', async () => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({ providers: [{ provide: ActivatedRoute, useValue: { snapshot: { data: { breadcrumb: 'nav.devices', milestone: 'M3' } } } }] });
      const { host } = await render(ComingSoonPage);
      expect(text(host, 'h1')).toBe('Devices');
      expect(host.textContent).toContain('milestone M3');
    });
  });
});
