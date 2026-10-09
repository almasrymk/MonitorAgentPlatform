import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, provideRouter } from '@angular/router';

import { ReportData } from '../core/api/reports.api';
import { AuthService } from '../core/auth/auth.service';
import { ScopeStore } from '../core/state/scope.store';
import { render, text } from '../testing/render';
import { ArchiveView } from './archive/archive.page';
import { AuditPage } from './audit/audit.page';
import { LocationContext } from './locations/location-context';
import { LocationSettingsPage } from './locations/location-settings.page';
import { PlatformArchivePage } from './platform/platform-archive.page';
import { PlatformReportsPage, cellText, toCsv } from './platform/platform-reports.page';
import { PlatformSettingsPage } from './platform/platform-settings.page';
import { ReportsPanel, fileSize } from './reports/reports-panel';
import { SettingsPage } from './settings/settings.page';

const settle = async () => {
  for (let i = 0; i < 10; i++) {
    await Promise.resolve();
  }
};

const types = (entitled = true) => ({
  pdfAvailable: false,
  types: [
    { type: 'overview', title: 'Overview Report', feature: 'reports.basic', advanced: false, entitled: true },
    { type: 'location-summary', title: 'Location Summary', feature: 'reports.basic', advanced: false, entitled: true },
    { type: 'device-health', title: 'Device Health', feature: 'reports.basic', advanced: false, entitled: true },
    { type: 'performance', title: 'Performance', feature: 'reports.advanced', advanced: true, entitled },
    { type: 'network-usage', title: 'Network Usage', feature: 'reports.advanced', advanced: true, entitled },
    { type: 'alerts', title: 'Alerts', feature: 'reports.basic', advanced: false, entitled: true },
    { type: 'license-usage', title: 'License Usage', feature: 'reports.basic', advanced: false, entitled: true },
    { type: 'custom', title: 'Custom', feature: 'reports.advanced', advanced: true, entitled },
  ],
});

const report = (overrides: Record<string, unknown> = {}) => ({
  id: 'r1', type: 'overview', title: 'Overview Report 2026-10-01 - 2026-10-08', format: 'Csv', status: 'Done', sizeBytes: 2048, requestedAt: '2026-10-08T07:00:00Z',
  completedAt: '2026-10-08T07:00:05Z', error: null, ...overrides,
});

const paged = <T>(items: T[]) => ({ items, total: items.length, page: 1, pageSize: 20 });

function signIn(permissions: string[], role = 'Administrator', tenantId: string | null = 't1'): void {
  TestBed.inject(AuthService).user.set({ id: 'u1', email: 'admin@acme.test', fullName: 'Admin', role, tenantId, tenantName: 'Acme', language: 'en', permissions, locationIds: [] } as never);
}

describe('M9 reports, archive and settings', () => {
  let http: HttpTestingController;

  /** Flushes every pending request for the path `part` (with any query; and fails if there is none). */
  const reply = (part: string, body: object | null, method = 'GET'): TestRequest[] => {
    const requests = http.match((r) => r.method === method && (r.url === part || (part.endsWith('?') && r.urlWithParams.startsWith(part))));
    expect(requests.length, `${method} ${part}`).toBeGreaterThan(0);
    requests.forEach((r) => r.flush(body));
    return requests;
  };

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), LocationContext, { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: new Map(), data: {} } } }],
    });
    http = TestBed.inject(HttpTestingController);
  });

  it('fileSize, cellText and toCsv format report values', () => {
    expect(fileSize(500)).toBe('500 B');
    expect(fileSize(2048)).toBe('2 KB');
    expect(fileSize(1.5 * 1024 * 1024)).toBe('1.5 MB');
    expect(cellText(1.234, 'en-GB')).toBe('1.23');
    expect(cellText(null, 'en-GB')).toBe('');
    expect(cellText('2026-10-08T07:00:00Z', 'en-GB')).toContain('2026');
    const data = { title: 'T', from: '', to: '', summary: [], columns: [{ key: 'a', label: 'Name', numeric: false }, { key: 'b', label: 'N', numeric: true }], rows: [['a,"b"', 1], ['=cmd', -2]] } as ReportData;
    expect(toCsv(data)).toBe('﻿Name,N\r\n"a,""b""",1\r\n\'=cmd,-2\r\n');
  });

  it('ReportsPanel lists the types, generates a report with the chosen options and lists it', async () => {
    signIn(['reports.read', 'reports.generate', 'locations.read', 'devices.read']);
    const { fixture, host } = await render(ReportsPanel);
    await settle();
    reply('/api/v1/reports/types', types());
    await settle();
    reply('/api/v1/locations', paged([{ id: 'l1', name: 'Cairo HQ' }]));
    await settle();
    reply('/api/v1/reports?', paged([report()]));
    reply('/api/v1/devices', paged([{ id: 'd1', name: 'WEB-SRV-01', locationId: 'l1' }]));
    await settle();
    fixture.detectChanges();

    expect(host.querySelectorAll('[data-testid^="type-"]').length).toBe(8);
    expect(host.querySelector('[data-testid="report-r1"]')?.textContent).toContain('Overview Report');
    expect((host.querySelector('[data-testid="report-format"] option[value="pdf"]') as HTMLOptionElement).disabled).toBe(true);

    (host.querySelector('[data-testid="type-device-health"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    (host.querySelector('[data-testid="generate-report"]') as HTMLButtonElement).click();
    await settle();
    const post = http.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/reports');
    expect(post.request.body.type).toBe('device-health');
    expect(post.request.body.format).toBe('csv');
    expect(post.request.body.groupBy).toBe('device');
    post.flush(report({ id: 'r2', type: 'device-health', status: 'Queued' }));
    await settle();
    reply('/api/v1/reports?', paged([report({ id: 'r2', status: 'Queued' }), report()]));
    await settle();
    fixture.detectChanges();
    expect(host.querySelector('[data-testid="report-r2"]')?.textContent).toContain('Queued');
    fixture.destroy();
  });

  it('ReportsPanel for a device shows only the device report types and fixes the device', async () => {
    signIn(['reports.read', 'reports.generate']);
    const { fixture, host } = await render(ReportsPanel, { deviceId: 'd9' });
    await settle();
    reply('/api/v1/reports/types', types(false));
    await settle();
    reply('/api/v1/reports?', paged([]));
    await settle();
    fixture.detectChanges();

    const shown = [...host.querySelectorAll('[data-testid^="type-"]')].map((b) => b.getAttribute('data-testid'));
    expect(shown).toEqual(['type-device-health', 'type-performance', 'type-network-usage', 'type-alerts', 'type-custom']);
    expect((host.querySelector('[data-testid="type-performance"]') as HTMLButtonElement).disabled).toBe(true);
    expect(host.querySelector('[data-testid="report-device"]')).toBeNull();
    (host.querySelector('[data-testid="generate-report"]') as HTMLButtonElement).click();
    await settle();
    const post = http.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/reports');
    expect(post.request.body.deviceIds).toEqual(['d9']);
    fixture.destroy();
  });

  it('ReportsPanel cannot generate without reports.generate', async () => {
    signIn(['reports.read']);
    const { fixture, host } = await render(ReportsPanel, { locationId: 'l1' });
    await settle();
    reply('/api/v1/reports/types', types());
    await settle();
    reply('/api/v1/reports?', paged([]));
    reply('/api/v1/devices', paged([]));
    await settle();
    fixture.detectChanges();

    expect((host.querySelector('[data-testid="generate-report"]') as HTMLButtonElement).disabled).toBe(true);
    expect(host.querySelector('[data-testid="type-license-usage"]')).toBeNull();
    fixture.destroy();
  });

  it('PlatformReportsPage shows the summary and the table of the chosen report', async () => {
    const { fixture, host } = await render(PlatformReportsPage);
    await settle();
    reply('/api/v1/platform/reports/overview', {
      title: 'Overview Report', from: '', to: '', summary: [{ label: 'Devices', value: '12' }],
      columns: [{ key: 'customer', label: 'Customer', numeric: false }, { key: 'devices', label: 'Devices', numeric: true }], rows: [['Acme', 10], ['Beta', 2]],
    });
    await settle();
    fixture.detectChanges();

    expect(text(host, '[data-testid="report-summary"]')).toContain('12');
    expect(host.querySelectorAll('[data-testid="report-table"] tbody tr').length).toBe(2);
    (host.querySelector('[data-testid="type-alerts"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await settle();
    reply('/api/v1/platform/reports/alerts', { title: 'Alerts', from: '', to: '', summary: [], columns: [], rows: [] });
  });

  const archiveData = (seesInternal: boolean) => {
    reply('/api/v1/archive/files', [{ id: 'f1', displayName: 'Contract', sizeBytes: 1000, uploadedAt: '2026-10-01T00:00:00Z', isInternal: false }]);
    return async () => {
      await settle();
      reply('/api/v1/archive/profile', {
        companyName: 'Acme Corporation', industry: 'Logistics', website: 'https://acme.test', phone: null, address: null, accountManager: 'Support User', customerSince: '2024-01-01', status: 'Active',
      });
      reply('/api/v1/archive/contacts', [{ id: 'c1', name: 'Nadia', email: 'nadia@acme.test', phone: null, jobTitle: 'IT', isPrimary: true }]);
      reply('/api/v1/archive/notes', [{ id: 'n1', body: 'Hello', isInternal: false, authorName: 'Admin', createdAt: '2026-10-01T00:00:00Z' }]);
      reply('/api/v1/archive/files', [{ id: 'f1', displayName: 'Contract', sizeBytes: 1000, uploadedAt: '2026-10-01T00:00:00Z', isInternal: false }]);
      if (seesInternal) {
        reply('/api/v1/archive/remote-access', [{ id: 'ra1', locationId: null, deviceId: null, tool: 'AnyDesk', label: 'Front desk', identifier: '12••••89', hasPassword: true, createdAt: '' }]);
      }
      await settle();
    };
  };

  it('ArchiveView for a customer has no Remote Access tab and shows the company details', async () => {
    signIn(['archive.read', 'archive.manage']);
    const { fixture, host } = await render(ArchiveView);
    await settle();
    await archiveData(false)();
    fixture.detectChanges();

    const tabs = [...host.querySelectorAll('[role="tab"]')].map((t) => t.textContent?.trim());
    expect(tabs).not.toContain('Remote Access');
    expect(text(host, '[data-testid="company-details"]')).toContain('Acme Corporation');
    expect(text(host, '[data-testid="recent-files"]')).toContain('Contract');
    http.expectNone('/api/v1/archive/remote-access');
  });

  it('ArchiveView for platform staff reveals a remote access entry', async () => {
    signIn(['archive.read', 'archive.manage', 'archive.internal'], 'PlatformSupport', null);
    const { fixture, host } = await render(ArchiveView);
    await settle();
    await archiveData(true)();
    reply('/api/v1/locations', paged([]));
    reply('/api/v1/devices', paged([]));
    fixture.detectChanges();

    const remoteTab = [...host.querySelectorAll('[role="tab"]')].find((t) => t.textContent?.includes('Remote Access')) as HTMLElement;
    remoteTab.click();
    fixture.detectChanges();
    expect(text(host, '[data-testid="remote-access"]')).toContain('12••••89');
    (host.querySelector('[data-testid="reveal-ra1"]') as HTMLButtonElement).click();
    await settle();
    reply('/api/v1/archive/remote-access/ra1/reveal', { id: 'ra1', identifier: '123 456 789', password: 'DEV-ONLY-x' });
    await settle();
    fixture.detectChanges();
    expect(text(host, '[data-testid="remote-access"]')).toContain('123 456 789');
  });

  it('ArchiveView shows "not in your plan" for a plan without the archive', async () => {
    signIn(['archive.read']);
    const { fixture, host } = await render(ArchiveView);
    await settle();
    http.expectOne('/api/v1/archive/files').flush({ code: 'FEATURE_NOT_ENTITLED', status: 403 }, { status: 403, statusText: 'Forbidden' });
    await settle();
    fixture.detectChanges();

    expect(host.querySelector('[data-testid="archive-not-entitled"]')).not.toBeNull();
  });

  it('PlatformSettingsPage loads and saves the settings', async () => {
    const { fixture, host } = await render(PlatformSettingsPage);
    await settle();
    reply('/api/v1/platform/settings', { brandName: 'Monitor', offlineAlertDelayMinutes: 2, minuteRetentionDays: 30, hourRetentionDays: 400, emailSenderName: 'M', emailSenderAddress: 'a@b.test' });
    await settle();
    fixture.detectChanges();
    await fixture.whenStable();

    (host.querySelector('[data-testid="save-platform-settings"]') as HTMLButtonElement).click();
    await settle();
    const put = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/v1/platform/settings');
    expect(put.request.body.minuteRetentionDays).toBe(30);
    put.flush(put.request.body);
  });

  it('AuditPage lists the tenant audit', async () => {
    const { fixture, host } = await render(AuditPage);
    await settle();
    reply('/api/v1/audit', paged([{ id: 'a1', tenantId: 't1', actorType: 'User', actorId: 'u1', actorName: 'Admin', action: 'report.requested', entityType: 'GeneratedReport', entityId: 'r1', success: true, details: 'overview (Csv)', ip: null, at: '2026-10-08T07:00:00Z' }]));
    await settle();
    fixture.detectChanges();

    expect(text(host, 'mc-data-table')).toContain('report.requested');
    http.expectNone('/api/v1/platform/audit');
  });

  it('LocationSettingsPage saves the location with If-Match', async () => {
    signIn(['locations.read', 'locations.manage']);
    TestBed.inject(LocationContext).id.set('l1');
    const { fixture, host } = await render(LocationSettingsPage);
    await settle();
    reply('/api/v1/locations/l1', {
      id: 'l1', name: 'Cairo HQ', code: 'CAIRO-HQ', city: 'Cairo', country: 'Egypt', addressLine: null, timeZone: 'Africa/Cairo', contactName: null, contactEmail: null, contactPhone: null,
      isDefault: false, status: 'Active', createdAt: '', version: 'AAA=',
    });
    await settle();
    fixture.detectChanges();
    await fixture.whenStable();

    (host.querySelector('[data-testid="save-location"]') as HTMLButtonElement).click();
    await settle();
    const put = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/v1/locations/l1');
    expect(put.request.headers.get('If-Match')).toBe('"AAA="');
    expect(put.request.body.addressLine).toBeNull();
  });

  it('SettingsPage Integrations keeps, replaces or removes the secret', async () => {
    signIn(['settings.manage']);
    const { fixture, host } = await render(SettingsPage);
    await settle();
    reply('/api/v1/locations', paged([{ id: 'l1', name: 'Cairo HQ', city: 'Cairo', devices: 3, isDefault: false }]));
    reply('/api/v1/settings/general', { timeZone: 'Africa/Cairo', defaultLanguage: 'en', offlineAlertSeverity: 'Critical', offlineAlertDelayMinutes: 2 });
    reply('/api/v1/settings/alerts', { emailEnabled: true, inAppEnabled: true, webhookEnabled: true, webhookUrl: 'https://hooks.acme.test', emailEntitled: true, webhookEntitled: true });
    reply('/api/v1/settings/alerts/recipients', []);
    await settle();
    reply('/api/v1/settings/monitoring', { telemetry: { sampleSeconds: 5 }, thresholds: { cpu: {}, ram: {}, disk: {}, tempC: {} }, features: { remoteActions: false } });
    await settle();
    reply('/api/v1/settings/integrations', { webhookEnabled: true, webhookUrl: 'https://hooks.acme.test', hasSecret: true, webhookEntitled: true });
    await settle();
    fixture.detectChanges();

    const tab = [...host.querySelectorAll('[role="tab"]')].find((t) => t.textContent?.includes('Integrations')) as HTMLElement;
    tab.click();
    fixture.detectChanges();
    await fixture.whenStable();
    (host.querySelector('[data-testid="save-integrations"]') as HTMLButtonElement).click();
    await settle();
    const put = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/v1/settings/integrations');
    expect(put.request.body).toEqual({ webhookEnabled: true, webhookUrl: 'https://hooks.acme.test', secret: null });
    put.flush({ webhookEnabled: true, webhookUrl: 'https://hooks.acme.test', hasSecret: true, webhookEntitled: true });
    await settle();
    reply('/api/v1/settings/alerts', { emailEnabled: true, inAppEnabled: true, webhookEnabled: true, webhookUrl: 'https://hooks.acme.test', emailEntitled: true, webhookEntitled: true });
    await settle();
    fixture.detectChanges();

    (host.querySelector('[data-testid="test-webhook"]') as HTMLButtonElement).click();
    await settle();
    reply('/api/v1/settings/integrations/webhook/test', { success: false, statusCode: 500, error: 'HTTP 500' }, 'POST');
    await settle();
    fixture.detectChanges();
    expect(text(host, '[data-testid="webhook-test-result"]')).toContain('HTTP 500');

    const locationsTab = [...host.querySelectorAll('[role="tab"]')].find((t) => t.textContent?.includes('Locations')) as HTMLElement;
    locationsTab.click();
    fixture.detectChanges();
    expect(host.querySelector('[data-testid="location-settings-l1"]')).not.toBeNull();
  });

  it('PlatformArchivePage opens the workspace with a reason on the Archive screen', async () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const { fixture, host } = await render(PlatformArchivePage);
    await settle();
    reply('/api/v1/platform/tenants', paged([
      { id: 't1', name: 'Acme', code: 'ACME', status: 'Active', city: 'Cairo', country: 'Egypt', planCode: 'ENTERPRISE', planName: 'Enterprise' },
      { id: 't2', name: 'Small', code: 'SMALL', status: 'Active', city: 'Giza', country: 'Egypt', planCode: 'STARTER', planName: 'Starter' },
    ]));
    await settle();
    fixture.detectChanges();

    expect(host.querySelector('[data-testid="open-archive-SMALL"]')).toBeNull();
    (host.querySelector('[data-testid="open-archive-ACME"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const reason = host.querySelector('[data-testid="reason"]') as HTMLTextAreaElement;
    reason.value = 'Customer asked for their contract';
    reason.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (host.querySelector('[data-testid="confirm"]') as HTMLButtonElement).click();
    await settle();
    const post = http.expectOne('/api/v1/platform/tenants/t1/workspace-sessions');
    expect(post.request.body.reason).toBe('Customer asked for their contract');
    post.flush(null);
    await settle();
    expect(TestBed.inject(ScopeStore).workspace()?.id).toBe('t1');
    expect(navigate).toHaveBeenCalledWith(['/admin/customers', 't1', 'archive']);
  });
});
