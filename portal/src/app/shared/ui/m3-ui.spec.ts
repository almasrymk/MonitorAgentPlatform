import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { LocationCard as LocationCardData, TenantCard } from '../../core/api/models';
import { I18nService } from '../../core/i18n/i18n.service';
import { areaRoot } from '../../core/layout/area';
import { BreadcrumbLabels } from '../../core/layout/breadcrumb-labels';
import { device } from '../../testing/fixtures';
import { render, text } from '../../testing/render';
import { percent, relativeTime, uptime } from '../format';
import { CopyButton } from './copy-button';
import { CountBadge } from './count-badge';
import { CustomerCard } from './customer-card';
import { DeviceCard } from './device-card';
import { DonutChart } from './donut-chart';
import { HBar } from './hbar';
import { HealthBar } from './health-bar';
import { LocationCard } from './location-card';
import { Menu } from './menu';
import { OsIcon } from './os-icon';
import { ProgressBar } from './progress-bar';
import { RingGauge } from './ring-gauge';
import { ViewToggle } from './view-toggle';



describe('M3 design-system components', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
  });

  describe('formatting', () => {
    it('formats relative times, uptimes and percentages', () => {
      const i18n = TestBed.inject(I18nService);
      const now = Date.parse('2026-10-06T08:00:00Z');
      expect(relativeTime(i18n, null)).toBe('—');
      expect(relativeTime(i18n, '2026-10-06T07:59:30Z', now)).toBe('just now');
      expect(relativeTime(i18n, '2026-10-06T07:55:00Z', now)).toBe('5 min ago');
      expect(relativeTime(i18n, '2026-10-06T05:00:00Z', now)).toBe('3 h ago');
      expect(relativeTime(i18n, '2026-10-04T08:00:00Z', now)).toBe('2 d ago');
      expect(uptime(i18n, null)).toBe('—');
      expect(uptime(i18n, 90061)).toBe('1d 1h');
      expect(uptime(i18n, 3720)).toBe('1h 2m');
      expect(uptime(i18n, 480)).toBe('8m');
      expect(percent(12.345)).toBe('12.3%');
      expect(percent(null)).toBe('—');
    });

    it('finds the area root of a url', () => {
      expect(areaRoot('/app/locations/1/devices?x=1')).toBe('/app');
      expect(areaRoot('/admin/customers/t-1/locations')).toBe('/admin/customers/t-1');
      expect(areaRoot('/admin/plans')).toBe('/admin');
    });

    it('keeps breadcrumb labels', () => {
      const labels = TestBed.inject(BreadcrumbLabels);
      labels.set(':location', 'Cairo HQ');
      const first = labels.values();
      labels.set(':location', 'Cairo HQ');
      expect(labels.values()).toBe(first);
      expect(labels.values()[':location']).toBe('Cairo HQ');
    });
  });

  describe('ProgressBar', () => {
    it('clamps and shows the value', async () => {
      const { fixture, host } = await render(ProgressBar, { value: 120, label: 'CPU', tone: 'cpu' });
      expect(text(host, '[data-testid="progress-value"]')).toBe('100%');
      expect(host.querySelector('.fill')?.getAttribute('data-tone')).toBe('cpu');
      fixture.componentRef.setInput('value', null);
      fixture.detectChanges();
      expect(text(host, '[data-testid="progress-value"]')).toBe('—');
    });
  });

  describe('HealthBar', () => {
    it('has levels by percentage', async () => {
      const { fixture, host } = await render(HealthBar, { value: 85 });
      expect(fixture.componentInstance.level()).toBe('success');
      fixture.componentRef.setInput('value', 60);
      expect(fixture.componentInstance.level()).toBe('warning');
      fixture.componentRef.setInput('value', 10);
      fixture.detectChanges();
      expect(fixture.componentInstance.level()).toBe('danger');
      expect(text(host, '[data-testid="health-value"]')).toBe('10%');
    });
  });

  describe('RingGauge', () => {
    it('draws the value and names it', async () => {
      const { host } = await render(RingGauge, { value: 88.84, label: 'Health Score' });
      expect(text(host, '[data-testid="ring-value"]')).toBe('88.8%');
      expect(host.querySelector('.fill')?.getAttribute('stroke-dasharray')).toBe('88.84 100');
      expect(host.querySelector('svg')?.getAttribute('aria-label')).toBe('Health Score 88.8%');
    });

    it('shows a dash without a value', async () => {
      const { host } = await render(RingGauge, { value: null });
      expect(text(host, '[data-testid="ring-value"]')).toBe('—');
    });
  });

  describe('CountBadge', () => {
    it('is coloured by severity', async () => {
      const { fixture } = await render(CountBadge, { count: 3, severity: 'Critical', label: '3 open alerts' });
      const el = fixture.nativeElement as HTMLElement;
      expect(el.textContent?.trim()).toBe('3');
      expect(el.getAttribute('data-severity')).toBe('critical');
      expect(el.getAttribute('aria-label')).toBe('3 open alerts');
    });
  });

  describe('ViewToggle', () => {
    it('switches between grid and list', async () => {
      const { fixture, host } = await render(ViewToggle);
      (host.querySelector('[data-testid="view-list"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(fixture.componentInstance.view()).toBe('list');
      expect(host.querySelector('[data-testid="view-list"]')?.getAttribute('aria-pressed')).toBe('true');
    });
  });

  describe('CopyButton', () => {
    it('copies the text and confirms', async () => {
      const writeText = vi.fn().mockResolvedValue(undefined);
      Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
      const { fixture, host } = await render(CopyButton, { text: 'LOC-ABC234-XYZ789' });
      await fixture.componentInstance.copy();
      fixture.detectChanges();
      expect(writeText).toHaveBeenCalledWith('LOC-ABC234-XYZ789');
      expect(text(host, '[data-testid="copy"]')).toContain('Copied');
    });

    it('stays quiet when the clipboard is not available', async () => {
      Object.defineProperty(navigator, 'clipboard', { value: { writeText: vi.fn().mockRejectedValue(new Error('denied')) }, configurable: true });
      const { fixture } = await render(CopyButton, { text: 'x' });
      await fixture.componentInstance.copy();
      expect(fixture.componentInstance.copied()).toBe(false);
    });
  });

  describe('OsIcon', () => {
    it('names the operating system and falls back to Other', async () => {
      const { fixture, host } = await render(OsIcon, { os: 'MacOS' });
      expect(host.querySelector('svg')?.getAttribute('aria-label')).toBe('macOS');
      expect((fixture.nativeElement as HTMLElement).getAttribute('data-os')).toBe('macos');
      fixture.componentRef.setInput('os', 'BeOS');
      fixture.detectChanges();
      expect(host.querySelector('svg')?.getAttribute('aria-label')).toBe('Other');
    });
  });

  describe('HBar', () => {
    it('lists the rows with count and percentage', async () => {
      const { host } = await render(HBar, { items: [{ key: 'w', label: 'Windows', value: 98, percent: 69 }, { key: 'l', label: 'Linux', value: 28, percent: 19.7, color: 'mc-series-linux' }] });
      const rows = host.querySelectorAll('[data-testid="hbar-row"]');
      expect(rows.length).toBe(2);
      expect(rows[1].textContent).toContain('28');
      expect(rows[1].textContent).toContain('(19.7%)');
    });
  });

  describe('DonutChart', () => {
    it('shows the total, a legend with percentages and a text summary', async () => {
      const { fixture, host } = await render(DonutChart, {
        segments: [
          { key: 'h', label: 'Healthy', value: 126, color: 'mc-success' },
          { key: 'w', label: 'Warning', value: 6, color: 'mc-warning' },
          { key: 'c', label: 'Critical', value: 2, color: 'mc-danger' },
          { key: 'o', label: 'Offline', value: 8, color: 'mc-neutral' },
        ],
        centerLabel: 'Devices',
      });
      expect(text(host, '[data-testid="donut-total"]')).toBe('142');
      const legend = host.querySelectorAll('[data-testid="donut-legend"]');
      expect(legend.length).toBe(4);
      expect(legend[0].textContent).toContain('88.7%');
      expect(host.querySelector('[role="img"]')?.getAttribute('aria-label')).toContain('Healthy 126 (88.7%)');
      fixture.componentRef.setInput('segments', []);
      fixture.detectChanges();
      expect(text(host, '[data-testid="donut-total"]')).toBe('0');
    });
  });

  describe('Menu', () => {
    it('opens, emits the chosen item and closes on an outside click', async () => {
      const { fixture, host } = await render(Menu, { items: [{ id: 'rename', label: 'Rename' }, { id: 'retire', label: 'Retire', danger: true }], label: 'Actions' });
      const chosen: string[] = [];
      fixture.componentInstance.selected.subscribe((id) => chosen.push(id));
      (host.querySelector('[data-testid="menu-trigger"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      (host.querySelector('[data-testid="menu-retire"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(chosen).toEqual(['retire']);
      expect(fixture.componentInstance.open()).toBe(false);

      fixture.componentInstance.toggle();
      document.body.click();
      expect(fixture.componentInstance.open()).toBe(false);
    });
  });

  describe('DeviceCard', () => {
    it('shows the device, its alert badge, bars and footer actions', async () => {
      const { fixture, host } = await render(DeviceCard, { device: device(), canManage: true });
      const opened: boolean[] = [];
      fixture.componentInstance.open.subscribe((e) => opened.push(e.live));
      expect(text(host, '[data-testid="device-name"]')).toBe('WEB-SRV-01');
      expect(text(host, '[data-testid="device-status"]')).toBe('Critical');
      expect(text(host, '[data-testid="device-license"]')).toBe('Licensed');
      expect(text(host, '[data-testid="alert-badge"]')).toBe('3');
      expect(text(host, '[data-testid="last-seen"]')).toBe('just now');
      expect(host.textContent).toContain('3d 4h');
      (host.querySelector('[data-testid="open-console"]') as HTMLButtonElement).click();
      (host.querySelector('[data-testid="device-details"]') as HTMLButtonElement).click();
      expect(opened).toEqual([true, false]);
    });

    it('disables the console when offline and emits menu actions', async () => {
      const { fixture, host } = await render(DeviceCard, { device: device({ connection: 'Offline', health: 'Unknown', openAlerts: 0, licenseState: 'Unlicensed' }), canManage: true });
      const actions: string[] = [];
      fixture.componentInstance.action.subscribe((e) => actions.push(e.action));
      expect((host.querySelector('[data-testid="open-console"]') as HTMLButtonElement).disabled).toBe(true);
      expect(text(host, '[data-testid="device-status"]')).toBe('Offline');
      expect(host.querySelector('[data-testid="alert-badge"]')).toBeNull();
      (host.querySelector('[data-testid="menu-trigger"]') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect((host.querySelector('[data-testid="menu-unlicense"]') as HTMLButtonElement).disabled).toBe(true);
      (host.querySelector('[data-testid="menu-move"]') as HTMLButtonElement).click();
      expect(actions).toEqual(['move']);
    });

    it('has no menu without the manage permission', async () => {
      const { host } = await render(DeviceCard, { device: device({ health: 'Unknown' }), canManage: false });
      expect(host.querySelector('mc-menu')).toBeNull();
      expect(text(host, '[data-testid="device-status"]')).toBe('Online');
    });
  });

  const location = (overrides: Partial<LocationCardData> = {}): LocationCardData => ({
    id: 'l1', name: 'Cairo HQ', code: 'CAIRO-HQ', city: 'Cairo', country: 'Egypt', isDefault: false, status: 'Active', devices: 142, online: 134, warning: 6, critical: 2, healthScore: 88.7, ...overrides,
  });

  describe('LocationCard', () => {
    it('links to the location and shows its counts and health', async () => {
      const { host } = await render(LocationCard, { location: location(), link: ['/app', 'locations', 'l1', 'overview'] });
      expect(host.querySelector('a')?.getAttribute('href')).toBe('/app/locations/l1/overview');
      expect(text(host, '[data-testid="location-devices"]')).toBe('142');
      expect(host.textContent).toContain('Cairo, Egypt');
      expect(host.textContent).toContain('Online');
      expect(text(host, '[data-testid="ring-value"]')).toBe('88.7%');
    });

    it('names the default location Unassigned and shows offline without devices online', async () => {
      const { host } = await render(LocationCard, { location: location({ isDefault: true, online: 0, city: null, country: null, healthScore: null }), link: ['x'], variant: 'horizontal' });
      expect(host.textContent).toContain('Unassigned');
      expect(host.textContent).toContain('Offline');
      expect(host.querySelector('mc-ring-gauge')).toBeNull();
    });
  });

  const tenant = (overrides: Partial<TenantCard> = {}): TenantCard => ({
    id: 't1', name: 'Acme Corporation', code: 'ACME', status: 'Active', city: 'Cairo', country: 'Egypt', customerSince: '2024-01-01', locations: 3, planCode: 'ENTERPRISE', planName: 'Enterprise',
    devices: 316, healthy: 247, warning: 36, critical: 15, healthScore: 78.2, licensesUsed: 290, licenseLimit: 500, nextRenewal: '2027-01-15T00:00:00Z', subscriptionStatus: 'Active', expiringSoon: false, ...overrides,
  });

  describe('CustomerCard', () => {
    it('shows plan, device health counts, health ring and licence usage', async () => {
      const { host } = await render(CustomerCard, { card: tenant(), canManage: true });
      expect(text(host, '[data-testid="plan-name"]')).toBe('Enterprise');
      expect(host.querySelector('[data-testid="customer-devices"]')?.getAttribute('data-count')).toBe('316');
      expect(text(host, '[data-testid="customer-critical"]')).toBe('15');
      expect(text(host, '[data-testid="ring-value"]')).toBe('78.2%');
      expect(text(host, '[data-testid="usage-text"]')).toBe('290 / 500');
      expect(host.textContent).toContain('Suspend');
    });

    it('shows Expiring and offers Reactivate for suspended customers', async () => {
      const { fixture, host } = await render(CustomerCard, { card: tenant({ expiringSoon: true }), canManage: true });
      expect(host.querySelector('mc-status-pill')?.textContent?.trim()).toBe('Expiring');
      const actions: string[] = [];
      fixture.componentInstance.action.subscribe((a) => actions.push(a));
      fixture.componentRef.setInput('card', tenant({ status: 'Suspended', healthScore: 40 }));
      fixture.detectChanges();
      expect(fixture.componentInstance.tone()).toBe('danger');
      const reactivate = Array.from(host.querySelectorAll('button')).find((b) => b.textContent?.includes('Reactivate')) as HTMLButtonElement;
      reactivate.click();
      (host.querySelector('[data-testid="open-workspace"]') as HTMLButtonElement).click();
      expect(actions).toEqual(['reactivate', 'workspace']);
    });

    it('hides management buttons without the permission', async () => {
      const { host } = await render(CustomerCard, { card: tenant({ nextRenewal: null, devices: null }), canManage: false });
      expect(host.textContent).not.toContain('Suspend');
      expect(text(host, '[data-testid="renewal"]')).toBe('—');
      expect(host.querySelector('[data-testid="customer-devices"]')?.getAttribute('data-count')).toBe('0');
    });
  });
});
