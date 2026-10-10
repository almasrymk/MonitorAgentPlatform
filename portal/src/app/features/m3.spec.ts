import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { AuthResult, LocationDashboard, TenantDashboard } from '../core/api/models';
import { AuthService } from '../core/auth/auth.service';
import { device } from '../testing/fixtures';
import { render, text } from '../testing/render';
import { CustomerDashboardPage } from './dashboard/customer-dashboard.page';
import { AddDeviceDialog } from './devices/add-device-dialog';
import { DevicesBrowser } from './devices/devices-browser';
import { LocationContext } from './locations/location-context';
import { LocationOverviewPage } from './locations/location-overview.page';
import { LocationPage } from './locations/location.page';
import { CustomersPage } from './platform/customers.page';

const settle = async () => {
  for (let i = 0; i < 8; i++) {
    await Promise.resolve();
  }
};

const tile = (key: string, value: number, delta: number | null = null, percentOfTotal: number | null = null) => ({ key, value, delta, deltaPercent: null, percentOfTotal });

export const tenantDashboard: TenantDashboard = {
  header: { tenantId: 't1', name: 'Acme Corporation', status: 'Active', planName: 'Enterprise', locations: 3, devices: 316, customerSince: '2024-01-01' },
  tiles: [tile('locations', 3), tile('devices', 316, 12), tile('online', 298, null, 94.3), tile('healthy', 247), tile('warning', 36), tile('critical', 15), tile('licensed', 290), tile('unlicensed', 26)],
  locations: [{ id: 'l1', name: 'Cairo HQ', code: 'CAIRO-HQ', city: 'Cairo', country: 'Egypt', isDefault: false, status: 'Active', devices: 142, online: 134, warning: 6, critical: 2, healthScore: 88.7 }],
  locationsHealth: { total: 316, healthy: 247, warning: 36, critical: 15, offline: 18 },
  incidentTrend: [],
  topProblematicDevices: [{ id: 'd1', name: 'WEB-SRV-01', locationId: 'l1', locationName: 'Cairo HQ', issue: 'critical', issueTitle: null, health: 'Critical', connection: 'Online', openAlerts: 3, lastSeenAt: null }],
  deviceStatusByLocation: [{ locationId: 'l1', name: 'Cairo HQ', total: 142, online: 134, healthy: 126, warning: 6, critical: 2, healthPercent: 88.7 }],
  recentAlerts: [],
  license: { licensed: 290, unlicensed: 26, planName: 'Enterprise', used: 290, limit: 500, renewsAt: '2027-01-15T00:00:00Z' },
};

const locationDashboard: LocationDashboard = {
  location: {
    id: 'l1', name: 'Cairo HQ', code: 'CAIRO-HQ', city: 'Cairo', country: 'Egypt', addressLine: '1 Nile Street', timeZone: 'Africa/Cairo', contactName: 'Ops Desk', contactEmail: null, contactPhone: '+20 100',
    isDefault: false, customerName: 'Acme Corporation', planName: 'Enterprise', customerSince: '2024-01-01', lastSyncAt: '2026-10-06T07:59:00Z',
  },
  tiles: [tile('devices', 142), tile('online', 134, null, 94.4), tile('healthy', 126), tile('warning', 6), tile('critical', 2), tile('licensed', 138)],
  incidentTrend: [],
  devicesByOs: [{ name: 'Windows', count: 98, percent: 69 }, { name: 'Linux', count: 28, percent: 19.7 }, { name: 'MacOS', count: 12, percent: 8.5 }, { name: 'Other', count: 4, percent: 2.8 }],
  deviceHealth: { total: 142, healthy: 126, warning: 6, critical: 2, offline: 8 },
  resources: { onlineDevices: 134, cpu: 31.2, ram: 51.8, disk: 52.4, healthScore: 88.7 },
  topProblematicDevices: [{ id: 'd1', name: 'WEB-SRV-01', locationId: 'l1', locationName: 'Cairo HQ', issue: 'critical', issueTitle: null, health: 'Critical', connection: 'Online', openAlerts: 3, lastSeenAt: null }],
  recentAlerts: [],
};

const page = <T,>(items: T[], total = items.length) => ({ items, total, page: 1, pageSize: 24 });

describe('M3 screens', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    // The Devices screen also loads the open alerts of Recent Device Alerts.
    http.match((r) => r.url === '/api/v1/alerts').forEach((r) => r.flush({ items: [], total: 0, page: 1, pageSize: 6 }));
    http.verify();
  });

  async function signIn(permissions: string[], role = 'Administrator'): Promise<void> {
    const auth = TestBed.inject(AuthService);
    const done = auth.login('admin@acme.test', 'pw');
    const body: AuthResult = { accessToken: 'a', refreshToken: 'r', expiresAt: '', user: { id: '1', fullName: 'A', email: 'a', role, tenantId: 't1', tenantName: 'Acme', permissions, language: 'en', locationIds: [] } };
    http.expectOne('/api/v1/auth/login').flush(body);
    await done;
  }

  describe('CustomerDashboardPage', () => {
    it('shows the header, eight tiles, locations, problem devices, status by location and licence summary', async () => {
      const { fixture, host } = await render(CustomerDashboardPage);
      http.expectOne((r) => r.url === '/api/v1/dashboard').flush(tenantDashboard);
      await settle();
      fixture.detectChanges();

      expect(host.querySelector('mc-entity-header')?.textContent).toContain('Acme Corporation');
      expect(host.querySelector('mc-entity-header')?.textContent).toContain('3 Locations');
      expect(host.querySelectorAll('[data-testid="dashboard-tiles"] mc-kpi-tile').length).toBe(8);
      expect(text(host, '[data-testid="tile-devices"] [data-testid="kpi-value"]')).toBe('316');
      expect(text(host, '[data-testid="tile-devices"] [data-testid="kpi-delta"]')).toContain('+12');
      expect(host.querySelector('[data-testid="tile-online"]')?.textContent).toContain('94.3% of total');
      expect(host.querySelectorAll('mc-location-card').length).toBe(1);
      expect(host.textContent).toContain('Critical alert open');
      expect(host.querySelector('[data-testid="status-by-location"]')?.textContent).toContain('142');
      expect(text(host, '[data-testid="locations-health"] [data-testid="donut-total"]')).toBe('316');
      expect(host.querySelector('[data-testid="license-summary"]')?.textContent).toContain('290 / 500');
    });

    it('shows the error state and retries', async () => {
      const { fixture, host } = await render(CustomerDashboardPage);
      http.expectOne((r) => r.url === '/api/v1/dashboard').flush({}, { status: 500, statusText: 'Error' });
      await settle();
      fixture.detectChanges();
      expect(host.querySelector('mc-error-state')).not.toBeNull();
      void fixture.componentInstance.load();
      http.expectOne((r) => r.url === '/api/v1/dashboard').flush(tenantDashboard);
      await settle();
      fixture.detectChanges();
      expect(host.querySelector('mc-error-state')).toBeNull();
    });
  });

  describe('LocationPage and LocationOverviewPage', () => {
    it('loads the location once for the header and the overview tab', async () => {
      const { fixture, host } = await render(LocationPage, { id: 'l1' });
      await settle();
      http.expectOne((r) => r.url === '/api/v1/locations/l1/dashboard').flush(locationDashboard);
      await settle();
      fixture.detectChanges();
      expect(host.querySelector('mc-entity-header')?.textContent).toContain('Cairo HQ');
      expect(host.querySelector('mc-entity-header')?.textContent).toContain('Acme Corporation');
    });

    it('shows tiles, OS and health donuts, resource rings, problems and the summary', async () => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), LocationContext] });
      http = TestBed.inject(HttpTestingController);
      TestBed.inject(LocationContext).dashboard.set(locationDashboard);
      const { fixture, host } = await render(LocationOverviewPage);
      fixture.detectChanges();

      expect(host.querySelectorAll('[data-testid="location-tiles"] mc-kpi-tile').length).toBe(6);
      expect(text(host, '[data-testid="devices-by-os"] [data-testid="donut-total"]')).toBe('142');
      expect(host.querySelector('[data-testid="devices-by-os"]')?.textContent).toContain('Windows');
      expect(text(host, '[data-testid="device-health"] [data-testid="donut-total"]')).toBe('142');
      expect(host.querySelectorAll('[data-testid="resources"] mc-ring-gauge').length).toBe(4);
      expect(host.querySelector('[data-testid="resources"]')?.textContent).toContain('Across 134 online devices');
      expect(host.textContent).toContain('WEB-SRV-01');
      expect(host.querySelector('[data-testid="location-summary"]')?.textContent).toContain('1 Nile Street, Cairo, Egypt');
      expect(host.querySelector('[data-testid="location-summary"]')?.textContent).toContain('Synced successfully');
    });
  });

  describe('DevicesBrowser', () => {
    async function open(inputs: Record<string, unknown>, permissions = ['devices.read', 'devices.manage', 'devices.enroll', 'locations.read']) {
      await signIn(permissions);
      const rendered = await render(DevicesBrowser, inputs);
      await settle();
      return rendered;
    }

    it('lists devices with tiles and filters by location, OS, status and licence', async () => {
      const { fixture, host } = await open({ locationId: 'l1', showSummary: true, allowEnroll: true });
      http.expectOne((r) => r.url === '/api/v1/locations').flush(page([]));
      const list = http.expectOne((r) => r.url === '/api/v1/devices');
      expect(list.request.params.get('locationId')).toBe('l1');
      expect(list.request.params.get('sort')).toBe('severity');
      list.flush(page([device(), device({ id: 'd2', name: 'DB-SRV-01', health: 'Warning' })], 142));
      http.expectOne((r) => r.url === '/api/v1/devices/summary').flush({ total: 142, online: 134, offline: 8, licensed: 138, unlicensed: 4, healthy: 126, warning: 6, critical: 2, newDevices: 3 });
      await settle();
      fixture.detectChanges();

      expect(host.querySelectorAll('[data-testid="device-card"]').length).toBe(2);
      expect(text(host, '[data-testid="tile-offline"] [data-testid="kpi-value"]')).toBe('8');
      expect(host.querySelector('[data-testid="add-device"]')).not.toBeNull();

      const browser = fixture.componentInstance as unknown as { setFilter(apply: () => void): void; os: { set(v: string): void }; status: { set(v: string): void } };
      browser.setFilter(() => {
        browser.os.set('linux');
        browser.status.set('critical');
      });
      await settle();
      const filtered = http.expectOne((r) => r.url === '/api/v1/devices');
      expect(filtered.request.params.get('os')).toBe('linux');
      expect(filtered.request.params.get('status')).toBe('critical');
      filtered.flush(page([]));
      http.expectOne((r) => r.url === '/api/v1/devices/summary').flush({ total: 0, online: 0, offline: 0, licensed: 0, unlicensed: 0, healthy: 0, warning: 0, critical: 0, newDevices: 0 });
      await settle();
      fixture.detectChanges();
      expect(host.querySelector('mc-empty-state')?.textContent).toContain('No devices match the filters.');
    });

    it('renames, moves, unlicenses and retires through the dialog', async () => {
      const { fixture, host } = await open({});
      http.expectOne((r) => r.url === '/api/v1/locations').flush(page([{ id: 'l2', name: 'Alexandria Branch', code: 'ALEX', city: null, country: null, isDefault: false, status: 'Active', devices: 0, online: 0, warning: 0, critical: 0, healthScore: null }]));
      http.expectOne((r) => r.url === '/api/v1/devices').flush(page([device()]));
      await settle();
      fixture.detectChanges();
      const browser = fixture.componentInstance as unknown as {
        act(e: { action: string; device: unknown }): void; updateName(n: string): void; updateLocation(id: string): void; confirm(): Promise<void>;
      };

      browser.act({ action: 'rename', device: device() });
      fixture.detectChanges();
      expect(host.querySelector('[data-testid="rename-input"]')).not.toBeNull();
      browser.updateName('  Reception PC ');
      let done = browser.confirm();
      const rename = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/v1/devices/d1');
      expect(rename.request.body).toEqual({ name: 'Reception PC', locationId: 'l1' });
      rename.flush(device({ name: 'Reception PC' }));
      await settle();
      http.expectOne((r) => r.url === '/api/v1/devices').flush(page([device({ name: 'Reception PC' })]));
      await done;

      browser.act({ action: 'move', device: device() });
      browser.updateLocation('l2');
      done = browser.confirm();
      const move = http.expectOne((r) => r.method === 'PUT');
      expect(move.request.body).toEqual({ name: 'WEB-SRV-01', locationId: 'l2' });
      move.flush(device());
      await settle();
      http.expectOne((r) => r.url === '/api/v1/devices').flush(page([device()]));
      await done;

      for (const action of ['unlicense', 'retire']) {
        browser.act({ action, device: device() });
        done = browser.confirm();
        http.expectOne((r) => r.method === 'POST' && r.url === `/api/v1/devices/d1/${action}`).flush(null);
        await settle();
        http.expectOne((r) => r.url === '/api/v1/devices').flush(page([]));
        await done;
      }
    });

    it('updates cards in place from live device state', async () => {
      const { fixture, host } = await open({});
      http.expectOne((r) => r.url === '/api/v1/locations').flush(page([]));
      http.expectOne((r) => r.url === '/api/v1/devices').flush(page([device()]));
      await settle();
      fixture.detectChanges();
      const browser = fixture.componentInstance as unknown as { applyLive(e: object): void };

      browser.applyLive({ deviceId: 'd1', locationId: 'l1', connection: 'Offline', health: 'Unknown', licenseState: 'Licensed', cpu: null, ram: null, disk: null, lastSeenAt: null });
      browser.applyLive({ deviceId: 'other', locationId: 'l1', connection: 'Online', health: 'Healthy', licenseState: 'Licensed', cpu: 1, ram: 1, disk: 1, lastSeenAt: null });
      fixture.detectChanges();

      expect(text(host, '[data-testid="device-status"]')).toBe('Offline');
      expect(host.querySelectorAll('[data-testid="device-card"]').length).toBe(1);
    });

    it('navigates to the device screen', async () => {
      const { fixture } = await open({ showLocationFilter: true }, ['devices.read']);
      http.expectOne((r) => r.url === '/api/v1/devices').flush(page([device()]));
      await settle();
      const router = TestBed.inject(Router);
      const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
      (fixture.componentInstance as unknown as { open(e: { device: unknown; live: boolean }): void }).open({ device: device(), live: true });
      expect(navigate).toHaveBeenCalledWith(['/admin', 'devices', 'd1'], { queryParams: { live: 1 } });
    });
  });

  describe('AddDeviceDialog', () => {
    it('creates an enrollment code and shows the install command per OS once', async () => {
      const { fixture, host } = await render(AddDeviceDialog, { open: true, locationId: 'l1' });
      const created: string[] = [];
      fixture.componentInstance.created.subscribe((c) => created.push(c.code));
      const done = fixture.componentInstance.create();
      const request = http.expectOne('/api/v1/locations/l1/enrollment-codes');
      expect(request.request.body).toEqual({ expiresInHours: 24 });
      request.flush({
        id: 'c1', code: 'LOC-ABC234-XYZ789', expiresAt: '2026-10-07T08:00:00Z', maxUses: null,
        installCommands: { windows: 'msiexec LOCATION="LOC-ABC234-XYZ789"', linux: 'curl ... LOC-ABC234-XYZ789 bash', macOs: 'installer LOC-ABC234-XYZ789' },
      });
      await done;
      fixture.detectChanges();

      expect(text(host, '[data-testid="enrollment-code"]')).toBe('LOC-ABC234-XYZ789');
      expect(text(host, '[data-testid="install-command"]')).toContain('msiexec');
      (host.querySelector('[data-testid="tab-linux"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(text(host, '[data-testid="install-command"]')).toContain('curl');
      expect(created).toEqual(['LOC-ABC234-XYZ789']);

      (fixture.componentInstance as unknown as { setOpen(open: boolean): void }).setOpen(false);
      expect(fixture.componentInstance.code()).toBeNull();
    });
  });

  describe('CustomersPage health filter', () => {
    it('sends the health filter and renders customer cards', async () => {
      await signIn(['platform.tenants.read'], 'PlatformAdmin');
      const { fixture, host } = await render(CustomersPage);
      http.expectOne('/api/v1/platform/tenants/summary').flush({ total: 48, active: 47, expiringSoon: 6, suspended: 1 });
      http.expectOne((r) => r.url === '/api/v1/platform/tenants').flush(page([]));
      await settle();
      const customers = fixture.componentInstance as unknown as { setFilter(apply: () => void): void; health: { set(v: string): void } };
      customers.setFilter(() => customers.health.set('critical'));
      http.expectOne('/api/v1/platform/tenants/summary').flush({ total: 48, active: 47, expiringSoon: 6, suspended: 1 });
      const filtered = http.expectOne((r) => r.url === '/api/v1/platform/tenants');
      expect(filtered.request.params.get('health')).toBe('critical');
      filtered.flush(page([{
        id: 't1', name: 'Acme Corporation', code: 'ACME', status: 'Active', city: 'Cairo', country: 'Egypt', customerSince: '2024-01-01', locations: 3, planCode: 'ENTERPRISE', planName: 'Enterprise',
        devices: 316, healthy: 247, warning: 36, critical: 15, healthScore: 78.2, licensesUsed: 290, licenseLimit: 500, nextRenewal: null, subscriptionStatus: 'Active', expiringSoon: false,
      }]));
      await settle();
      fixture.detectChanges();
      expect(host.querySelectorAll('mc-customer-card').length).toBe(1);
      expect(text(host, '[data-testid="customer-critical"]')).toBe('15');
    });
  });
});
