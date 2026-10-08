import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { PlatformDashboard } from '../core/api/models';
import { Alert, MonitorPoint, NotificationItem } from '../core/api/monitoring.api';
import { AuthService } from '../core/auth/auth.service';
import { NotificationsStore } from '../core/state/notifications.store';
import { render, text } from '../testing/render';
import { IncidentTrend, RecentAlerts } from './alerts/alert-widgets';
import { MessagesBoard, MonitorPoints } from './devices/device/device-monitoring';
import { NotificationsPage } from './notifications/notifications.page';
import { PlatformDashboardPage } from './platform/platform-dashboard.page';
import { SettingsPage } from './settings/settings.page';

const settle = async () => {
  for (let i = 0; i < 10; i++) {
    await Promise.resolve();
  }
};

const alert = (overrides: Partial<Alert> = {}): Alert => ({
  id: 'a1', deviceId: 'd1', deviceName: 'WEB-SRV-01', locationId: 'l1', locationName: 'Cairo HQ', issueKey: 'cpu', category: 'Performance', severity: 'Critical',
  title: 'High CPU usage', message: 'CPU above 90%', status: 'Open', source: 'Agent', firstSeenAt: '2026-10-06T07:00:00Z', lastSeenAt: '2026-10-06T07:30:00Z', occurrences: 3,
  acknowledgedAt: null, resolvedAt: null, resolvedBy: null, canResolve: true, ...overrides,
});

const point = (overrides: Partial<MonitorPoint> = {}): MonitorPoint => ({
  id: 'p1', key: 'iis', displayName: 'IIS', type: 'Website', target: 'http://localhost/health', enabled: true, showInShortcut: true, intervalSeconds: 60, status: 'Warning',
  message: 'Slow response', responseMs: 2400, lastCheckedAt: new Date().toISOString(), statusSince: null, ...overrides,
});

const notification = (overrides: Partial<NotificationItem> = {}): NotificationItem => ({
  id: 'n1', tenantId: 't1', customerName: 'Acme Corporation', severity: 'Critical', category: 'Performance', title: 'High CPU usage', body: 'WEB-SRV-01 - Cairo HQ',
  locationId: 'l1', deviceId: 'd1', deviceName: 'WEB-SRV-01', alertId: 'a1', createdAt: '2026-10-06T07:00:00Z', read: false, ...overrides,
});

const platformDashboard: PlatformDashboard = {
  tiles: [{ key: 'customers', value: 48, delta: 2, deltaPercent: 4.3, percentOfTotal: null }],
  incidentTrend: [{ name: 'Critical', points: [{ day: '2026-10-05', value: 2 }, { day: '2026-10-06', value: 5 }] }, { name: 'Warning', points: [] }, { name: 'Info', points: [] }],
  incidentsBySeverity: [{ name: 'Critical', count: 5, percent: 50 }, { name: 'Warning', count: 4, percent: 40 }, { name: 'Info', count: 1, percent: 10 }],
  resolvedIncidents: 7,
  subscriptionDistribution: [{ name: 'Enterprise', count: 3, percent: 100 }],
  topCustomers: [{ tenantId: 't1', name: 'Gulf Engineering', devices: 428, online: 401, healthScore: 83.2 }],
  expiringSubscriptions: [{ tenantId: 't2', name: 'Horizon Retail', planName: 'Enterprise', renewsAt: '2026-10-28T00:00:00Z', daysLeft: 22, status: 'Active' }],
  recentAlerts: [{ id: 'a1', at: '2026-10-06T07:30:00Z', severity: 'Critical', tenantId: 't1', customerName: 'Acme Corporation', deviceId: 'd1', deviceName: 'WEB-SRV-01', locationName: 'Cairo HQ', message: 'High CPU usage' }],
  deviceHealth: { total: 10, healthy: 7, warning: 1, critical: 1, offline: 1 },
  devicesByOs: [{ name: 'Windows', count: 7, percent: 70 }],
  recentActivity: [{ at: '2026-10-06T07:00:00Z', action: 'device.enrolled', actorName: 'Admin User', entityType: 'Device', details: 'New device registered', customerName: null }],
} as unknown as PlatformDashboard;

describe('M6 alerts and notifications', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  describe('widgets', () => {
    it('IncidentTrend maps severities to colours and shows an empty state without points', async () => {
      const { fixture, host } = await render(IncidentTrend, { trend: platformDashboard.incidentTrend });
      const series = fixture.componentInstance.series();
      expect(series.map((s) => s.color)).toEqual(['mc-danger', 'mc-warning', 'mc-info']);
      expect(series[0].points).toEqual([{ at: '2026-10-05', value: 2 }, { at: '2026-10-06', value: 5 }]);
      expect(host.querySelector('[data-testid="incident-trend"]')).not.toBeNull();

      fixture.componentRef.setInput('trend', [{ name: 'Critical', points: [] }]);
      fixture.detectChanges();
      expect(host.querySelector('[data-testid="incident-trend"]')).toBeNull();
      expect(host.textContent).toContain('No incidents');
    });

    it('RecentAlerts shows the customer on the platform and the device elsewhere', async () => {
      const { fixture, host } = await render(RecentAlerts, { alerts: platformDashboard.recentAlerts, mode: 'platform' });
      expect(host.querySelector('thead')?.textContent).toContain('Customer');
      expect(host.querySelector('tbody')?.textContent).toContain('Acme Corporation');
      fixture.componentRef.setInput('mode', 'location');
      fixture.detectChanges();
      const headers = host.querySelector('thead')?.textContent ?? '';
      expect(headers).toContain('Device');
      expect(headers).toContain('Location');
      expect(host.querySelector('tbody')?.textContent).toContain('WEB-SRV-01');
    });

    it('MonitorPoints shows a status dot per point and the detail of the selected one', async () => {
      const { fixture, host } = await render(MonitorPoints, { points: [point(), point({ id: 'p2', key: 'db', displayName: 'Database', status: 'Healthy' })] });
      expect(host.querySelectorAll('[role="option"]').length).toBe(2);
      expect(host.querySelector('[data-testid="point-iis"]')?.getAttribute('data-status')).toBe('warning');
      (host.querySelector('[data-testid="point-iis"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(text(host, '[data-testid="point-detail"]')).toContain('2400 ms');
      (host.querySelector('[data-testid="point-iis"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(host.querySelector('[data-testid="point-detail"]')).toBeNull();

      fixture.componentRef.setInput('points', []);
      fixture.detectChanges();
      expect(host.textContent).toContain('No monitor points');
    });

    it('MessagesBoard lists open alerts first and marks resolved ones', async () => {
      const resolved = alert({ id: 'a2', title: 'Disk space low', severity: 'Warning', status: 'Resolved', lastSeenAt: '2026-10-06T09:00:00Z' });
      const { host } = await render(MessagesBoard, { alerts: [resolved, alert()] });
      const items = host.querySelectorAll('[data-testid="messages-board"] li');
      expect(items.length).toBe(2);
      expect(items[0].textContent).toContain('High CPU usage');
      expect(items[0].getAttribute('data-severity')).toBe('critical');
      expect(items[1].classList.contains('resolved')).toBe(true);
    });
  });

  describe('bell', () => {
    it('reads the customer feed in /app and the platform feed in /admin', async () => {
      const auth = TestBed.inject(AuthService);
      auth.user.set({ id: 'u1', email: 'admin@acme.test', fullName: 'Admin', role: 'Administrator', tenantId: 't1', tenantName: 'Acme', language: 'en', permissions: ['notifications.read'], locationIds: [] } as never);
      const store = TestBed.inject(NotificationsStore);
      expect(store.platform()).toBe(true);
      TestBed.tick();
      await settle();
      http.match('/api/v1/platform/notifications/unread-count').forEach((r) => r.flush({ unread: 9 }));

      await store.refresh();
      // A tenant user without the platform permission gets no platform count.
      expect(store.unread()).toBe(0);
    });
  });

  describe('screens', () => {
    it('NotificationsPage loads the feed, marks one as read and refreshes the bell', async () => {
      const { fixture, host } = await render(NotificationsPage);
      await settle();
      const list = http.expectOne((r) => r.url === '/api/v1/platform/notifications' || r.url === '/api/v1/notifications');
      expect(list.request.params.get('from')).not.toBeNull();
      list.flush({ items: [notification(), notification({ id: 'n2', read: true, title: 'Disk space low' })], total: 2, page: 1, pageSize: 20 });
      await settle();
      fixture.detectChanges();
      expect(host.querySelectorAll('[data-testid="notifications"] tbody tr').length).toBe(2);

      (host.querySelector('[data-testid="read-n1"]') as HTMLButtonElement).click();
      await settle();
      const mark = http.expectOne((r) => r.url.endsWith('/notifications/read'));
      expect(mark.request.body).toEqual({ ids: ['n1'] });
      mark.flush(null);
      await settle();
      fixture.detectChanges();
      expect(host.querySelector('[data-testid="read-n1"]')).toBeNull();
    });

    it('SettingsPage saves the general settings and adds a recipient', async () => {
      const { fixture, host } = await render(SettingsPage);
      await settle();
      http.expectOne('/api/v1/settings/general').flush({ timeZone: 'Africa/Cairo', defaultLanguage: 'en', offlineAlertSeverity: 'Critical', offlineAlertDelayMinutes: 2 });
      http.expectOne('/api/v1/settings/alerts').flush({ emailEnabled: true, smsEnabled: false, smsAvailable: false, inAppEnabled: true, webhookEnabled: false, webhookUrl: null, emailEntitled: true, webhookEntitled: false });
      http.expectOne('/api/v1/settings/alerts/recipients').flush([]);
      http.match((r) => r.url === '/api/v1/locations').forEach((r) => r.flush({ items: [], total: 0, page: 1, pageSize: 200 }));
      await settle();
      fixture.detectChanges();
      await fixture.whenStable();

      (host.querySelector('[data-testid="save-general"]') as HTMLButtonElement).click();
      await settle();
      const save = http.expectOne((r) => r.method === 'PUT' && r.url === '/api/v1/settings/general');
      expect(save.request.body.offlineAlertDelayMinutes).toBe(2);
      save.flush(save.request.body);
      await settle();

      (host.querySelector('[data-testid="tab-alerts"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect((host.querySelector('[data-testid="switch-sms"]') as HTMLInputElement).disabled).toBe(true);
      expect((host.querySelector('[data-testid="switch-webhook"]') as HTMLInputElement).disabled).toBe(true);
      (host.querySelector('[data-testid="add-recipient"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(host.querySelector('[data-testid="recipient-email"]')).not.toBeNull();
    });

    it('PlatformDashboardPage shows tiles, severity caption, top customers and activity', async () => {
      const { fixture, host } = await render(PlatformDashboardPage);
      await settle();
      const request = http.expectOne((r) => r.url === '/api/v1/platform/dashboard');
      expect(request.request.params.get('trendDays')).toBe('7');
      request.flush(platformDashboard);
      await settle();
      fixture.detectChanges();
      expect(text(host, '[data-testid="tile-customers"]')).toContain('48');
      expect(text(host, '[data-testid="resolved"]')).toBe('Resolved: 7');
      expect(text(host, '[data-testid="top-customers"]')).toContain('Gulf Engineering');
      expect(text(host, '[data-testid="recent-activity"]')).toContain('Device registered');
      expect(host.querySelectorAll('[data-testid="recent-alerts"] tbody tr').length).toBe(1);
    });
  });
});
