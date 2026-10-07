import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AuthResult } from '../core/api/models';
import { AuthService } from '../core/auth/auth.service';
import { UsageBar } from '../shared/ui/usage-bar';
import { render, text } from '../testing/render';
import { PlansPage } from './platform/plans.page';
import { SubscriptionPage } from './subscription/subscription.page';

const settle = async () => {
  for (let i = 0; i < 5; i++) {
    await Promise.resolve();
  }
};

const subscription = {
  planCode: 'ENTERPRISE', planName: 'Enterprise', status: 'Active', features: ['monitoring'], startsAt: '2025-01-01T00:00:00Z', renewsAt: '2027-01-15T00:00:00Z',
  daysRemaining: 101, expiringSoon: false, devicesUsed: 290, deviceLimit: 500, licensedDevices: 290, unlicensedDevices: 26, usageByOs: [], syncedAt: '2026-10-06T08:00:00Z', stale: false,
};

describe('M2 screens', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  async function signIn(permissions: string[]): Promise<void> {
    const auth = TestBed.inject(AuthService);
    const done = auth.login('a@monitor.local', 'pw');
    const body: AuthResult = { accessToken: 'a', refreshToken: 'r', expiresAt: '', user: { id: '1', fullName: 'A', email: 'a', role: 'PlatformAdmin', tenantId: null, tenantName: null, permissions, language: 'en', locationIds: [] } };
    http.expectOne('/api/v1/auth/login').flush(body);
    await done;
  }

  describe('UsageBar', () => {
    it('shows used / limit with levels', async () => {
      const { fixture, host } = await render(UsageBar, { used: 45, limit: 50 });
      expect(text(host, '[data-testid="usage-text"]')).toBe('45 / 50');
      expect(fixture.componentInstance.level()).toBe('warning');
      fixture.componentRef.setInput('used', 50);
      expect(fixture.componentInstance.level()).toBe('danger');
      fixture.componentRef.setInput('limit', null);
      fixture.detectChanges();
      expect(text(host, '[data-testid="usage-text"]')).toBe('50 / ∞');
      expect(fixture.componentInstance.percent()).toBe(0);
    });
  });

  describe('SubscriptionPage', () => {
    it('shows the plan, seat usage, renewal and licence counts', async () => {
      const { fixture, host } = await render(SubscriptionPage);
      http.expectOne('/api/v1/subscription').flush(subscription);
      await settle();
      fixture.detectChanges();

      expect(host.querySelector('[data-testid="current-plan"]')?.textContent).toContain('Enterprise');
      expect(host.textContent).toContain('290 of 500 devices used');
      expect(host.querySelector('[data-testid="renewal"]')?.textContent).toContain('101 days remaining');
      expect(host.querySelector('[data-testid="tab-licensed"]')?.textContent).toContain('(290)');
      expect(host.querySelector('[data-testid="tab-unlicensed"]')?.textContent).toContain('(26)');
      expect(host.querySelector('[data-testid="stale"]')).toBeNull();
    });

    it('shows Expiring and the stale warning and opens the contact dialog', async () => {
      const { fixture, host } = await render(SubscriptionPage);
      http.expectOne('/api/v1/subscription').flush({ ...subscription, expiringSoon: true, stale: true });
      await settle();
      fixture.detectChanges();

      expect(host.querySelector('mc-status-pill')?.textContent?.trim()).toBe('Expiring');
      expect(host.querySelector('[data-testid="stale"]')).not.toBeNull();
      (host.querySelector('[data-testid="upgrade"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(host.textContent).toContain('Contact your account manager');
    });

    it('shows the devices of a licence state in the device tabs', async () => {
      const { fixture, host } = await render(SubscriptionPage);
      http.expectOne('/api/v1/subscription').flush(subscription);
      await settle();
      (fixture.componentInstance as unknown as { tab: { set(v: string): void } }).tab.set('licensed');
      fixture.detectChanges();
      await settle();
      const request = http.expectOne((r) => r.url === '/api/v1/devices');
      expect(request.request.params.get('license')).toBe('licensed');
      request.flush({ items: [], total: 0, page: 1, pageSize: 24 });
      await settle();
      fixture.detectChanges();
      expect(host.querySelector('[data-testid="subscription-devices"] mc-empty-state')).not.toBeNull();
    });

    it('shows the error state', async () => {
      const { fixture, host } = await render(SubscriptionPage);
      http.expectOne('/api/v1/subscription').flush({}, { status: 500, statusText: 'Error' });
      await settle();
      fixture.detectChanges();
      expect(host.querySelector('mc-error-state')).not.toBeNull();
    });
  });

  describe('PlansPage', () => {
    it('lists plans with customer counts and syncs on demand', async () => {
      await signIn(['platform.plans.read', 'platform.licensing.sync']);
      const { fixture, host } = await render(PlansPage);
      http.expectOne('/api/v1/platform/plans').flush([{ code: 'STARTER', name: 'Starter', version: 1, features: ['monitoring', 'alerts'], deviceLimit: 50, durationDays: 365, price: 49, currency: 'USD', customers: 4 }]);
      http.expectOne('/api/v1/platform/licensing/status').flush({ mode: 'Fake', lastSuccessAt: '2026-10-06T08:00:00Z', lastAttemptAt: null, lastFullReconcileAt: null, lastError: null, consecutiveFailures: 0, degraded: false, portalUrl: 'http://localhost:4200' });
      await settle();
      fixture.detectChanges();

      expect(host.querySelectorAll('[data-testid="table-row"]').length).toBe(1);
      expect(host.textContent).toContain('monitoring, alerts');
      expect(host.querySelector('[data-testid="open-licensing"]')?.getAttribute('href')).toBe('http://localhost:4200');
      expect(text(host, '[data-testid="licensing-status"]')).toContain('Fake');

      const synced = (fixture.componentInstance as unknown as { sync(): Promise<void> }).sync();
      http.expectOne('/api/v1/platform/licensing/sync').flush({ succeeded: true, tenantsChecked: 48, tenantsChanged: 0, error: null });
      await settle();
      http.expectOne('/api/v1/platform/plans').flush([]);
      http.expectOne('/api/v1/platform/licensing/status').flush({ mode: 'Fake', lastSuccessAt: null, lastAttemptAt: null, lastFullReconcileAt: null, lastError: null, consecutiveFailures: 5, degraded: true, portalUrl: null });
      await synced;
      fixture.detectChanges();
      expect(text(host, '[data-testid="licensing-status"]')).toContain('Sync failing');
    });
  });
});
