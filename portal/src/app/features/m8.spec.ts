import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { ConfigDocument, DeviceConfiguration, MonitorPoint } from '../core/api/monitoring.api';
import { AuthService } from '../core/auth/auth.service';
import { render, text } from '../testing/render';
import { ThresholdsForm, editable, normalised } from './configuration/thresholds-form';
import { DeviceContext } from './devices/device/device-context';
import { DeviceMonitorPointsPage } from './devices/device/device-monitor-points.page';
import { DeviceSettingsPage } from './devices/device/device-settings.page';

const settle = async () => {
  for (let i = 0; i < 10; i++) {
    await Promise.resolve();
  }
};

const document = (): ConfigDocument => ({
  telemetry: { sampleSeconds: 5 },
  thresholds: {
    cpu: { warningPercent: 80, criticalPercent: 95, forSeconds: 300, clearBelowPercent: 75 },
    ram: { warningPercent: 80, criticalPercent: 95, forSeconds: 300, clearBelowPercent: 75 },
    disk: { warningPercent: 85, criticalPercent: 92, forSeconds: 60, clearBelowPercent: null },
    tempC: { critical: 85, forSeconds: 120 },
  },
  features: { remoteActions: false },
});

const configuration = (overrides: Partial<DeviceConfiguration> = {}): DeviceConfiguration => ({
  deviceId: 'd1', version: 3, document: document(), appliedVersion: 3, appliedAt: '2026-10-08T07:00:00Z', rejectedVersion: null, error: null, updatedAt: '2026-10-08T07:00:00Z', ...overrides,
});

const point = (overrides: Partial<MonitorPoint> = {}): MonitorPoint => ({
  id: 'p1', key: 'shop', displayName: 'Shop', type: 'Website', target: 'https://shop.acme.test', enabled: true, showInShortcut: true, intervalSeconds: 60, status: 'Healthy',
  message: 'OK', responseMs: 42, lastCheckedAt: new Date().toISOString(), statusSince: null, alertLevel: 'Problem', origin: 'Cloud', settings: null, version: 'AAAAAAAAB9E=', ...overrides,
});

function signIn(permissions: string[]): void {
  TestBed.inject(AuthService).user.set({ id: 'u1', email: 'it@acme.test', fullName: 'IT', role: 'ITManager', tenantId: 't1', tenantName: 'Acme', language: 'en', permissions, locationIds: [] } as never);
}

describe('M8 central configuration', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), DeviceContext] });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(DeviceContext).id.set('d1');
  });

  it('normalised turns typed values into numbers and an empty clear level into null', () => {
    const edited = editable(document());
    (edited.thresholds.cpu as unknown as Record<string, unknown>)['criticalPercent'] = '90';
    (edited.thresholds.cpu as unknown as Record<string, unknown>)['clearBelowPercent'] = '';

    const result = normalised(edited);

    expect(result.thresholds.cpu.criticalPercent).toBe(90);
    expect(result.thresholds.cpu.clearBelowPercent).toBeNull();
    expect(document().thresholds.cpu.criticalPercent).toBe(95);
  });

  it('ThresholdsForm is read-only without the permission', async () => {
    const { host } = await render(ThresholdsForm, { document: document(), readonly: true });
    await settle();

    expect((host.querySelector('[data-testid="cpu-critical"]') as HTMLInputElement).disabled).toBe(true);
    expect(host.querySelectorAll('tbody tr').length).toBe(4);
  });

  it('DeviceSettingsPage shows the versions and saves with If-Match', async () => {
    signIn(['devices.read', 'devices.configure']);
    const { fixture, host } = await render(DeviceSettingsPage);
    await settle();
    http.expectOne('/api/v1/devices/d1/configuration').flush(configuration());
    await settle();
    fixture.detectChanges();

    expect(text(host, '[data-testid="target-version"]')).toBe('3');
    expect(text(host, '[data-testid="config-status"]')).toBe('Applied');

    (host.querySelector('[data-testid="save-configuration"]') as HTMLButtonElement).click();
    await settle();
    const save = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/v1/devices/d1/configuration');
    expect(save.request.headers.get('If-Match')).toBe('"3"');
    save.flush(configuration({ version: 4, appliedVersion: 3 }));
    await settle();
    fixture.detectChanges();
    expect(text(host, '[data-testid="config-status"]')).toBe('Waiting for the agent');
  });

  it('DeviceSettingsPage reports a rejected version with its reason', async () => {
    signIn(['devices.read']);
    const { fixture, host } = await render(DeviceSettingsPage);
    await settle();
    http.expectOne('/api/v1/devices/d1/configuration').flush(configuration({ version: 5, appliedVersion: 4, rejectedVersion: 5, error: 'secretRef db-main is not stored' }));
    await settle();
    fixture.detectChanges();

    expect(text(host, '[data-testid="config-status"]')).toBe('Version 5 rejected');
    expect(text(host, '[data-testid="config-error"]')).toBe('secretRef db-main is not stored');
    expect(host.querySelector('[data-testid="save-configuration"]')).toBeNull();
  });

  it('DeviceMonitorPointsPage lists points and adds one with type settings', async () => {
    signIn(['devices.read', 'monitorpoints.manage']);
    const { fixture, host } = await render(DeviceMonitorPointsPage);
    await settle();
    http.expectOne('/api/v1/devices/d1/monitor-points').flush([point()]);
    await settle();
    fixture.detectChanges();
    expect(host.querySelectorAll('[data-testid="points-table"] tbody tr').length).toBe(1);

    (host.querySelector('[data-testid="add-point"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await fixture.whenStable();
    const page = fixture.componentInstance as unknown as { form: () => Record<string, unknown> | null; save: () => Promise<void> };
    Object.assign(page.form()!, { displayName: 'API', target: 'https://api.acme.test', expectStatus: 204 });
    void page.save();
    await settle();
    const created = http.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/devices/d1/monitor-points');
    expect(created.request.body.settings).toEqual({ expectStatus: 204 });
    expect(created.request.body.key).toBeNull();
    created.flush(point({ id: 'p2', key: 'cloud-1', displayName: 'API' }));
    await settle();
    http.expectOne('/api/v1/devices/d1/monitor-points').flush([point(), point({ id: 'p2', key: 'cloud-1', displayName: 'API' })]);
    await settle();
    fixture.detectChanges();
    expect(host.querySelectorAll('[data-testid="points-table"] tbody tr').length).toBe(2);
  });

  it('DeviceMonitorPointsPage hides the edit actions without the permission', async () => {
    signIn(['devices.read']);
    const { fixture, host } = await render(DeviceMonitorPointsPage);
    await settle();
    http.expectOne('/api/v1/devices/d1/monitor-points').flush([point()]);
    await settle();
    fixture.detectChanges();

    expect(host.querySelector('[data-testid="add-point"]')).toBeNull();
    expect(host.querySelector('[data-testid="edit-shop"]')).toBeNull();
  });
});
