import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { DeviceDetails, DeviceOverview } from '../core/api/models';
import { LiveService } from '../core/live/live.service';
import { AccordionItem, Timeline } from '../shared/ui/accordion';
import { Sparkline } from '../shared/ui/sparkline';
import { SpeedGauge } from '../shared/ui/speed-gauge';
import { TrendChart } from '../shared/ui/trend-chart';
import { render, text } from '../testing/render';
import { DeviceAboutPage } from './devices/device/device-about.page';
import { DeviceApplicationsPage } from './devices/device/device-applications.page';
import { DeviceContext } from './devices/device/device-context';
import { DeviceOverviewPage } from './devices/device/device-overview.page';
import { DevicePage } from './devices/device/device.page';

const settle = async () => {
  for (let i = 0; i < 8; i++) {
    await Promise.resolve();
  }
};

export const deviceDetails: DeviceDetails = {
  id: 'd1', name: 'WEB-SRV-01', hostname: 'WEB-SRV-01', fingerprint: 'acme-dev-0001', tenantId: 't1', customerName: 'Acme Corporation', locationId: 'l1', locationName: 'Cairo HQ',
  osFamily: 'Windows', osName: 'Windows Server 2019', osVersion: '10.0.17763', architecture: 'x64', localIp: '192.168.1.10', publicIp: '203.0.113.10', macAddress: null,
  agentVersion: '1.1.0', protocolVersion: 1, status: 'Active', enrolledAt: '2025-01-01T00:00:00Z', connection: 'Online', health: 'Critical', licenseState: 'Licensed', licenseReason: null,
  lastSeenAt: new Date().toISOString(), uptimeSeconds: 3600, appliedConfigVersion: 3, targetConfigVersion: null, version: 'v1',
};

const snapshot = {
  cpu: { usage: 87.7, physicalCores: 8, logicalCores: 16, speedGhz: 2.4, tempC: 53, processes: 263, model: 'Intel Xeon Silver 4314' },
  ram: { usage: 81.4, totalGb: 31.7, usedGb: 25.8, freeGb: 5.9, cachedGb: 2.1 },
  diskActivity: { activePercent: 8, readBps: 18_432, writeBps: 144_384, responseMs: 5.5 },
  partitions: [{ drive: 'C:', usedGb: 230, freeGb: 20 }],
  network: { adapter: 'Intel(R) Ethernet', downloadBps: 425_000, uploadBps: 165_000, publicIp: '203.0.113.10', localIp: '192.168.1.10', pingMs: 22, lossPercent: 0 },
  top: { cpu: [{ name: 'w3wp.exe', pid: 1075, value: 43.8 }], ram: [], disk: [], network: [] },
  service: { status: 'Running', uptimeSeconds: 1_052_880 },
};

const overview: DeviceOverview = { capturedAt: '2026-10-06T08:00:00Z', snapshot, monitorPoints: [], messages: [] } as unknown as DeviceOverview;

describe('M5 device screen', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), DeviceContext] });
    http = TestBed.inject(HttpTestingController);
  });

  describe('charts and small components', () => {
    it('TrendChart summarises its series for screen readers', async () => {
      const { fixture } = await render(TrendChart, {
        series: [{ name: 'CPU', color: 'mc-series-cpu', points: [{ at: '2026-10-06T07:00:00Z', value: 20 }, { at: '2026-10-06T08:00:00Z', value: 91 }] }, { name: 'RAM', color: 'mc-series-ram', points: [] }],
      });
      expect(fixture.componentInstance.summary()).toBe('CPU: last 91%, max 91%; RAM: no data');
      const option = fixture.componentInstance.option() as { series: { data: unknown[] }[] };
      expect(option.series[0].data.length).toBe(2);
    });

    it('Sparkline draws a path and nothing for one value', async () => {
      const { fixture } = await render(Sparkline, { values: [1, 5, 3] });
      expect(fixture.componentInstance.path()).toMatch(/^M0\.0,/);
      fixture.componentRef.setInput('values', [4]);
      expect(fixture.componentInstance.path()).toBe('');
    });

    it('SpeedGauge shows the value and caps the arc', async () => {
      const { fixture, host } = await render(SpeedGauge, { value: 150, max: 100 });
      expect(fixture.componentInstance.dash()).toBe('100 100');
      expect(text(host, '[data-testid="speed-value"]')).toBe('150 Mbps');
      fixture.componentRef.setInput('value', null);
      fixture.detectChanges();
      expect(text(host, '[data-testid="speed-value"]')).toBe('—');
    });

    it('AccordionItem toggles and Timeline lists items', async () => {
      const { fixture, host } = await render(AccordionItem, { title: 'Basic Hardware' });
      expect(host.querySelector('.content')).toBeNull();
      (host.querySelector('[data-testid="accordion-toggle"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(host.querySelector('[data-testid="accordion-toggle"]')?.getAttribute('aria-expanded')).toBe('true');
      const timeline = await render(Timeline, { items: [{ at: '2026-10-06', title: 'Device registered', tone: 'success' }, { at: '2026-10-05', title: 'User added', description: 'IT Manager' }] });
      expect(timeline.host.querySelectorAll('[data-testid="timeline-item"]').length).toBe(2);
      expect(timeline.host.textContent).toContain('IT Manager');
    });
  });

  describe('DevicePage', () => {
    it('loads the device, renews live mode while online and applies live samples', async () => {
      vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] });
      const { fixture, host } = await render(DevicePage, { deviceId: 'd1' });
      await settle();
      http.expectOne('/api/v1/devices/d1').flush(deviceDetails);
      http.expectOne('/api/v1/devices/d1/overview').flush(overview);
      http.expectOne('/api/v1/devices/d1/disks').flush([]);
      await settle();
      fixture.detectChanges();
      await settle();

      expect(text(host, '[data-testid="device-title"]')).toBe('WEB-SRV-01');
      expect(host.querySelector('[data-testid="device-header"]')?.textContent).toContain('Acme Corporation | Cairo HQ');
      expect(host.querySelector('[data-testid="offline-banner"]')).toBeNull();
      http.expectOne('/api/v1/devices/d1/live-sessions').flush(null);
      vi.advanceTimersByTime(30_000);
      http.expectOne('/api/v1/devices/d1/live-sessions').flush(null);

      const context = fixture.debugElement.injector.get(DeviceContext);
      TestBed.inject(LiveService).liveSample$.next({ deviceId: 'd1', at: '', cpu: 12, ram: 30, diskActive: 1, rxBps: 1, txBps: 1, cpuTempC: null });
      TestBed.inject(LiveService).liveSample$.next({ deviceId: 'other', at: '', cpu: 99, ram: 99, diskActive: 1, rxBps: 1, txBps: 1, cpuTempC: null });
      expect(context.live()?.cpu).toBe(12);

      TestBed.inject(LiveService).snapshot$.next({ deviceId: 'd1', capturedAt: '' });
      http.expectOne('/api/v1/devices/d1/overview').flush(overview);
      TestBed.inject(LiveService).deviceState$.next({ deviceId: 'd1', locationId: 'l1', connection: 'Offline', health: 'Unknown', licenseState: 'Licensed', cpu: null, ram: null, disk: null, lastSeenAt: null });
      http.expectOne('/api/v1/devices/d1').flush({ ...deviceDetails, connection: 'Offline', health: 'Unknown' });
      await settle();
      fixture.detectChanges();
      expect(host.querySelector('[data-testid="offline-banner"]')).not.toBeNull();
      vi.advanceTimersByTime(60_000);
      http.expectNone('/api/v1/devices/d1/live-sessions');
      vi.useRealTimers();
    });

    it('shows the error state', async () => {
      const { fixture, host } = await render(DevicePage, { deviceId: 'd1' });
      await settle();
      http.expectOne('/api/v1/devices/d1').flush({}, { status: 404, statusText: 'Not Found' });
      http.expectOne('/api/v1/devices/d1/overview').flush({});
      http.expectOne('/api/v1/devices/d1/disks').flush([]);
      await settle();
      fixture.detectChanges();
      expect(host.querySelector('mc-error-state')).not.toBeNull();
    });
  });

  describe('DeviceOverviewPage', () => {
    it('shows the snapshot, live values, history, disks and hardware', async () => {
      const context = TestBed.inject(DeviceContext);
      context.id.set('d1');
      context.device.set(deviceDetails);
      context.overview.set(overview);
      context.disks.set([{ drive: 'C:', label: 'System', fileSystem: 'NTFS', totalGb: 250, usedGb: 230, freeGb: 20, usagePercent: 92, status: 'Critical', at: '2026-10-06T08:00:00Z' }]);
      const { fixture, host } = await render(DeviceOverviewPage);
      await settle();
      const metrics = http.expectOne((r) => r.url === '/api/v1/devices/d1/metrics');
      expect(metrics.request.params.get('metrics')).toBe('cpu,ram,disk,network');
      metrics.flush({ resolution: 'hour', from: '', to: '', series: [{ metric: 'cpu', points: [{ at: '2026-10-06T07:00:00Z', avg: 40, max: 60 }] }] });
      http.expectOne('/api/v1/devices/d1/inventory/hardware').flush({ kind: 'Hardware', updatedAt: '', document: { manufacturer: 'Dell Inc.', cpu: { cores: 16 } } });
      http.expectOne('/api/v1/devices/d1/inventory/os').flush({}, { status: 404, statusText: 'Not Found' });
      http.expectOne('/api/v1/devices/d1/inventory/network').flush({}, { status: 404, statusText: 'Not Found' });
      await settle();
      fixture.detectChanges();

      expect(text(host, '[data-testid="gauge-cpu"] [data-testid="ring-value"]')).toBe('87.7%');
      expect(host.querySelector('[data-testid="metric-cpu"]')?.textContent).toContain('Intel Xeon Silver 4314');
      expect(host.querySelector('[data-testid="metric-cpu"]')?.textContent).toContain('w3wp.exe');
      expect(host.querySelector('[data-testid="service-status"]')?.textContent).toContain('Running');
      expect(host.querySelector('[data-testid="disk-status"]')?.textContent).toContain('Critical 92%');
      expect(host.querySelector('[data-testid="hardware-os"]')?.textContent).toContain('Dell Inc.');
      expect(host.querySelector('[data-testid="hardware-os"]')?.textContent).toContain('CPU · Cores');

      context.live.set({ deviceId: 'd1', at: '', cpu: 12.5, ram: 30, diskActive: 1, rxBps: 1_250_000, txBps: 1, cpuTempC: 60 });
      fixture.detectChanges();
      expect(text(host, '[data-testid="gauge-cpu"] [data-testid="ring-value"]')).toBe('12.5%');
      expect(text(host, '[data-testid="speed-value"]')).toBe('10 Mbps');
    });
  });

  describe('DeviceApplicationsPage', () => {
    it('lists inventory rows per kind and filters them', async () => {
      const context = TestBed.inject(DeviceContext);
      context.id.set('d1');
      const { fixture, host } = await render(DeviceApplicationsPage);
      await settle();
      http.expectOne('/api/v1/devices/d1/inventory/programs').flush({ kind: 'Programs', updatedAt: '', document: [{ name: 'Notepad++', version: '8.6' }, { name: '7-Zip', version: '23' }] });
      await settle();
      fixture.detectChanges();
      expect(host.querySelectorAll('[data-testid="table-row"]').length).toBe(2);

      (fixture.componentInstance as unknown as { search: { set(v: string): void } }).search.set('zip');
      fixture.detectChanges();
      expect(host.querySelectorAll('[data-testid="table-row"]').length).toBe(1);

      (host.querySelector('[data-testid="tab-services"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      await settle();
      http.expectOne('/api/v1/devices/d1/inventory/services').flush({}, { status: 404, statusText: 'Not Found' });
      await settle();
      fixture.detectChanges();
      expect(host.querySelector('mc-empty-state')).not.toBeNull();
    });
  });

  describe('DeviceAboutPage', () => {
    it('masks the fingerprint', async () => {
      TestBed.inject(DeviceContext).device.set(deviceDetails);
      const { host } = await render(DeviceAboutPage);
      expect(text(host, '[data-testid="fingerprint"]')).toBe('acme••••0001');
      expect(host.textContent).toContain('1.1.0');
    });
  });
});
