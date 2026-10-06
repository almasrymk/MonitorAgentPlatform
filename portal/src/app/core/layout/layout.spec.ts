import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { AuthResult } from '../api/models';
import { AuthService } from '../auth/auth.service';
import { ScopeStore } from '../state/scope.store';
import { render, text } from '../../testing/render';
import { AppShell } from './app-shell';
import { CUSTOMER_MENU, PLATFORM_MENU } from './navigation';
import { Sidebar } from './sidebar';
import { Topbar } from './topbar';
import { WorkspaceBanner } from './workspace-banner';

describe('layout', () => {
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

  it('sidebar shows only the items the user may use', async () => {
    await signIn('ReportViewer', ['dashboard.read', 'locations.read', 'devices.read', 'reports.read'], 't');
    const { host } = await render(Sidebar, { items: CUSTOMER_MENU, base: '/app' });
    const links = Array.from(host.querySelectorAll('a')).map((a) => a.getAttribute('data-testid'));
    expect(links).toEqual(['nav-overview', 'nav-locations', 'nav-devices', 'nav-reports']);
    expect(host.querySelector('[data-testid="nav-users"]')).toBeNull();
    expect(host.querySelector('[data-testid="nav-settings"]')).toBeNull();
    expect(text(host, '[data-testid="version"]')).toMatch(/^Monitor Agent Platform v/);
  });

  it('sidebar renders the back link of a workspace', async () => {
    await signIn('PlatformAdmin', ['users.manage'], null);
    const { host } = await render(Sidebar, { items: CUSTOMER_MENU, base: '/admin/customers/x', backLink: { key: 'nav.customers', path: '/admin/customers' } });
    expect(text(host, '[data-testid="sidebar-back"]')).toContain('Customers');
    expect(host.querySelector('[data-testid="nav-users"]')?.getAttribute('href')).toBe('/admin/customers/x/users');
  });

  it('topbar shows the user, switches language and signs out', async () => {
    await signIn('Administrator', [], 't');
    const { fixture, host } = await render(Topbar);
    expect(text(host, '.user-name')).toBe('Admin User');
    expect(text(host, '.user-role')).toBe('Administrator');
    (host.querySelector('[data-testid="user-menu"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    (host.querySelector('[data-testid="language-switch"]') as HTMLButtonElement).click();
    (await vi.waitFor(() => http.expectOne('/api/v1/auth/me/language'))).flush(null);
    await vi.waitFor(() => expect(document.documentElement.dir).toBe('rtl'));
    const back = TestBed.inject(AuthService).setLanguage('en');
    (await vi.waitFor(() => http.expectOne('/api/v1/auth/me/language'))).flush(null);
    await back;
  });

  it('workspace banner shows the customer and leaves the workspace', async () => {
    const scope = TestBed.inject(ScopeStore);
    scope.enterWorkspace({ id: 'acme', name: 'Acme Corporation' });
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    const { fixture, host } = await render(WorkspaceBanner);
    expect(text(host, '[data-testid="workspace-banner"] span')).toBe('Viewing Acme Corporation as platform staff');
    const leaving = fixture.componentInstance.leave('acme');
    http.expectOne('/api/v1/platform/tenants/acme/workspace-sessions/current').flush(null);
    await leaving;
    expect(scope.workspace()).toBeNull();
    expect(navigate).toHaveBeenCalledWith('/admin/customers');
  });

  it('app shell picks the platform menu for platform users', async () => {
    await signIn('PlatformAdmin', PLATFORM_MENU.map((i) => i.permission!), null);
    const { host } = await render(AppShell);
    expect(host.querySelector('[data-testid="nav-customers"]')).not.toBeNull();
    expect(host.querySelector('[data-testid="content"]')).not.toBeNull();
  });

  it('app shell shows the customer menu inside a workspace', async () => {
    await signIn('PlatformAdmin', ['users.manage', 'locations.read'], null);
    TestBed.inject(ScopeStore).enterWorkspace({ id: 'acme', name: 'Acme' });
    await TestBed.inject(Router).navigateByUrl('/admin/customers/acme/locations');
    const { host } = await render(AppShell);
    expect(host.querySelector('[data-testid="nav-locations"]')?.getAttribute('href')).toBe('/admin/customers/acme/locations');
    expect(host.querySelector('[data-testid="workspace-banner"]')).not.toBeNull();
  });
});
