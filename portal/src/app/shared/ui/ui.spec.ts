import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { I18nService } from '../../core/i18n/i18n.service';
import { ToastService } from '../../core/ui/toast.service';
import { render, text } from '../../testing/render';
import { Avatar } from './avatar';
import { Button } from './button';
import { Card } from './card';
import { ConfirmWithReasonDialog } from './confirm-with-reason-dialog';
import { CellDef, DataTable } from './data-table';
import { Dialog } from './dialog';
import { Drawer } from './drawer';
import { EntityHeader, PageHeader } from './headers';
import { Icon } from './icon';
import { KpiTile } from './kpi-tile';
import { Pagination } from './pagination';
import { SearchInput } from './search-input';
import { Select } from './select';
import { Skeleton } from './skeleton';
import { EmptyState, ErrorState } from './states';
import { StatusPill } from './status-pill';
import { Tabs } from './tabs';
import { ToastHost } from './toast-host';

describe('design-system components', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
  });

  describe('KpiTile', () => {
    it('shows label, value and caption', async () => {
      const { host } = await render(KpiTile, { label: 'Total Devices', value: 316, caption: 'vs last 30 days' });
      expect(text(host, '.tile-label')).toBe('Total Devices');
      expect(text(host, '[data-testid="kpi-value"]')).toBe('316');
      expect(text(host, '.tile-caption')).toBe('vs last 30 days');
    });

    it('shows a dash without a value and no delta', async () => {
      const { host } = await render(KpiTile, { label: 'x', value: null });
      expect(text(host, '[data-testid="kpi-value"]')).toBe('—');
      expect(host.querySelector('[data-testid="kpi-delta"]')).toBeNull();
    });

    it('colours an increase green when up is good', async () => {
      const { host } = await render(KpiTile, { label: 'Healthy', value: 1, delta: 5, deltaPercent: 2.5, goodWhen: 'up' });
      const delta = host.querySelector('[data-testid="kpi-delta"]')!;
      expect(delta.getAttribute('data-trend')).toBe('good');
      expect(delta.textContent).toContain('+5 (+2.5%)');
    });

    it('colours an increase red when down is good', async () => {
      const { host } = await render(KpiTile, { label: 'Critical', value: 1, delta: 3, goodWhen: 'down' });
      expect(host.querySelector('[data-testid="kpi-delta"]')!.getAttribute('data-trend')).toBe('bad');
    });

    it('colours a decrease green when down is good and flat when zero', async () => {
      const down = await render(KpiTile, { label: 'Offline', value: 1, delta: -2, goodWhen: 'down' });
      expect(down.host.querySelector('[data-testid="kpi-delta"]')!.getAttribute('data-trend')).toBe('good');
      const flat = await render(KpiTile, { label: 'Offline', value: 1, delta: 0 });
      expect(flat.host.querySelector('[data-testid="kpi-delta"]')!.getAttribute('data-trend')).toBe('flat');
    });
  });

  describe('StatusPill', () => {
    it('maps statuses to tones and translated text', async () => {
      const { fixture, host } = await render(StatusPill, { status: 'Suspended' });
      expect(host.getAttribute('data-tone')).toBe('danger');
      expect(host.textContent?.trim()).toBe('Suspended');
      fixture.componentRef.setInput('status', 'online');
      fixture.detectChanges();
      expect(host.getAttribute('data-tone')).toBe('success');
    });

    it('uses neutral for unknown statuses and accepts a custom label', async () => {
      const { host } = await render(StatusPill, { status: 'mystery', label: 'Custom' });
      expect(host.getAttribute('data-tone')).toBe('neutral');
      expect(host.textContent?.trim()).toBe('Custom');
    });
  });

  describe('Avatar', () => {
    it('shows initials', async () => {
      const person = await render(Avatar, { name: 'Admin User' });
      expect(person.host.textContent).toBe('AU');
      const company = await render(Avatar, { name: 'Acme Corporation', letters: 1 });
      expect(company.host.textContent).toBe('A');
      const empty = await render(Avatar, { name: '  ' });
      expect(empty.host.textContent).toBe('?');
    });
  });

  describe('Icon', () => {
    it('renders a known path and falls back for unknown names', async () => {
      const { host } = await render(Icon, { name: 'bell' });
      expect(host.querySelector('path')?.getAttribute('d')).toContain('M18 8');
      expect(Icon.has('bell')).toBe(true);
      expect(Icon.has('nope')).toBe(false);
    });
  });

  describe('Card, headers and states', () => {
    it('renders the card title', async () => {
      const { host } = await render(Card, { title: 'Recent Alerts' });
      expect(text(host, '.card-title')).toBe('Recent Alerts');
    });

    it('renders page and entity headers', async () => {
      const page = await render(PageHeader, { title: 'Customers', subtitle: 'Manage' });
      expect(text(page.host, 'h1')).toBe('Customers');
      expect(text(page.host, 'p')).toBe('Manage');
      const entity = await render(EntityHeader, { title: 'Acme', status: 'Active', meta: ['Enterprise', '3 Locations'] });
      expect(text(entity.host, 'h1')).toBe('Acme');
      expect(entity.host.querySelectorAll('.sep').length).toBe(1);
    });

    it('renders empty and error states and emits retry', async () => {
      const empty = await render(EmptyState, { title: 'No data', message: 'Add one' });
      expect(text(empty.host, '.title')).toBe('No data');
      const error = await render(ErrorState);
      let retried = false;
      error.fixture.componentInstance.retry.subscribe(() => (retried = true));
      (error.host.querySelector('[data-testid="retry"]') as HTMLButtonElement).click();
      expect(retried).toBe(true);
      expect(error.host.getAttribute('role')).toBe('alert');
    });

    it('renders a skeleton with the given size', async () => {
      const { host } = await render(Skeleton, { height: 40, width: '50%' });
      expect(host.style.blockSize).toBe('40px');
      expect(host.getAttribute('aria-hidden')).toBe('true');
    });
  });

  describe('Button directive', () => {
    @Component({ imports: [Button], template: '<button mcButton="danger-outline" size="sm">x</button><button mcButton>y</button>', changeDetection: ChangeDetectionStrategy.OnPush })
    class Host {}

    it('applies variant and size classes', async () => {
      const { host } = await render(Host);
      const [first, second] = Array.from(host.querySelectorAll('button'));
      expect(first.className.split(' ').sort()).toEqual(['mc-btn', 'mc-btn--danger-outline', 'mc-btn--sm']);
      expect(second.className.split(' ').sort()).toEqual(['mc-btn', 'mc-btn--md', 'mc-btn--secondary']);
    });
  });

  describe('Select, Tabs and SearchInput', () => {
    it('Select updates its value', async () => {
      const { fixture, host } = await render(Select, { label: 'Plan', options: [{ value: 'a', label: 'A' }, { value: 'b', label: 'B' }], value: 'a' });
      const select = host.querySelector('select')!;
      select.value = 'b';
      select.dispatchEvent(new Event('change'));
      expect(fixture.componentInstance.value()).toBe('b');
    });

    it('Tabs marks the active tab and shows counts', async () => {
      const { fixture, host } = await render(Tabs, { tabs: [{ id: 'all', label: 'All', count: 8 }, { id: 'admins', label: 'Admins' }], active: 'all' });
      expect(host.querySelector('[data-testid="tab-all"]')!.getAttribute('aria-selected')).toBe('true');
      expect(text(host, '.count')).toBe('(8)');
      (host.querySelector('[data-testid="tab-admins"]') as HTMLButtonElement).click();
      expect(fixture.componentInstance.active()).toBe('admins');
    });

    it('SearchInput emits after a pause and immediately on Enter', async () => {
      vi.useFakeTimers();
      const { fixture, host } = await render(SearchInput, { placeholder: 'Search' });
      const values: string[] = [];
      fixture.componentInstance.searched.subscribe((v) => values.push(v));
      const input = host.querySelector('input')!;
      input.value = ' acme ';
      input.dispatchEvent(new Event('input'));
      expect(values).toEqual([]);
      vi.advanceTimersByTime(300);
      expect(values).toEqual(['acme']);
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
      expect(values).toEqual(['acme', 'acme']);
      vi.useRealTimers();
    });
  });

  describe('DataTable', () => {
    @Component({
      imports: [DataTable, CellDef],
      template: `<mc-data-table [columns]="columns" [rows]="rows()" [loading]="loading()" emptyText="Nothing" [(sort)]="sort">
        <ng-template mcCell="name" let-row><b class="custom">{{ $any(row).name }}</b></ng-template>
      </mc-data-table>`,
      changeDetection: ChangeDetectionStrategy.OnPush,
    })
    class Host {
      columns = [{ key: 'name', label: 'Name', sortable: true }, { key: 'city', label: 'City' }];
      rows = signal<{ id: string; name: string; city: string | null }[]>([{ id: '1', name: 'Cairo HQ', city: 'Cairo' }, { id: '2', name: 'Dubai', city: null }]);
      loading = signal(false);
      sort = signal<string | null>('name');
    }

    it('renders rows with cell templates and plain values', async () => {
      const { host } = await render(Host);
      expect(host.querySelectorAll('[data-testid="table-row"]').length).toBe(2);
      expect(text(host, '.custom')).toBe('Cairo HQ');
      expect(host.querySelectorAll('td')[1].textContent?.trim()).toBe('Cairo');
      expect(host.querySelectorAll('td')[3].textContent?.trim()).toBe('');
    });

    it('toggles the sort between ascending and descending', async () => {
      const { fixture, host } = await render(Host);
      const header = host.querySelector('th')!;
      expect(header.getAttribute('aria-sort')).toBe('ascending');
      (host.querySelector('.sort') as HTMLButtonElement).click();
      expect(fixture.componentInstance.sort()).toBe('-name');
      fixture.detectChanges();
      expect(header.getAttribute('aria-sort')).toBe('descending');
      (host.querySelector('.sort') as HTMLButtonElement).click();
      expect(fixture.componentInstance.sort()).toBe('name');
    });

    it('shows the empty text and the loading skeleton', async () => {
      const { fixture, host } = await render(Host);
      fixture.componentInstance.rows.set([]);
      fixture.detectChanges();
      expect(text(host, '.empty')).toBe('Nothing');
      fixture.componentInstance.loading.set(true);
      fixture.detectChanges();
      expect(host.querySelectorAll('mc-skeleton').length).toBe(5);
    });
  });

  describe('Pagination', () => {
    it('shows the range and moves between pages', async () => {
      const { fixture, host } = await render(Pagination, { page: 2, pageSize: 20, total: 45 });
      expect(text(host, '.summary')).toBe('Showing 21-40 of 45');
      expect(text(host, '.current')).toBe('Page 2 of 3');
      const pages: number[] = [];
      fixture.componentInstance.pageChange.subscribe((p) => pages.push(p));
      const [prev, next] = Array.from(host.querySelectorAll('button'));
      prev.click();
      next.click();
      expect(pages).toEqual([1, 3]);
    });

    it('disables both buttons on a single empty page', async () => {
      const { host } = await render(Pagination, { page: 1, pageSize: 20, total: 0 });
      expect(text(host, '.summary')).toBe('Showing 0-0 of 0');
      expect(Array.from(host.querySelectorAll('button')).every((b) => b.disabled)).toBe(true);
    });
  });

  describe('Dialog and Drawer', () => {
    it('opens, closes with the button and with Escape', async () => {
      const { fixture, host } = await render(Dialog, { title: 'Sample', open: true });
      expect(host.querySelector('[role="dialog"]')?.getAttribute('aria-label')).toBe('Sample');
      (host.querySelector('.close') as HTMLButtonElement).click();
      expect(fixture.componentInstance.open()).toBe(false);
      fixture.componentInstance.open.set(true);
      fixture.detectChanges();
      host.querySelector('[role="dialog"]')!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
      expect(fixture.componentInstance.open()).toBe(false);
    });

    it('drawer closes from the backdrop', async () => {
      const { fixture, host } = await render(Drawer, { title: 'Add Location', open: true });
      expect(text(host, 'h2')).toBe('Add Location');
      (host.querySelector('.backdrop') as HTMLElement).click();
      expect(fixture.componentInstance.open()).toBe(false);
    });
  });

  describe('ConfirmWithReasonDialog', () => {
    it('requires a reason of the minimum length before confirming', async () => {
      const { fixture, host } = await render(ConfirmWithReasonDialog, { title: 'Suspend', confirmLabel: 'Suspend', open: true, minLength: 10 });
      const reasons: string[] = [];
      fixture.componentInstance.confirmed.subscribe((r) => reasons.push(r));
      const confirm = host.querySelector('[data-testid="confirm"]') as HTMLButtonElement;
      expect(confirm.disabled).toBe(true);
      const textarea = host.querySelector('textarea')!;
      textarea.value = 'too short';
      textarea.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(confirm.disabled).toBe(true);
      textarea.value = '  Customer asked for help  ';
      textarea.dispatchEvent(new Event('input'));
      fixture.detectChanges();
      expect(confirm.disabled).toBe(false);
      confirm.click();
      expect(reasons).toEqual(['Customer asked for help']);
    });
  });

  describe('ToastHost', () => {
    it('shows toasts and dismisses them', async () => {
      const toasts = TestBed.inject(ToastService);
      const { fixture, host } = await render(ToastHost);
      toasts.error('Failed');
      toasts.success('Saved');
      fixture.detectChanges();
      expect(host.querySelectorAll('[data-testid="toast"]').length).toBe(2);
      (host.querySelector('[data-testid="toast"] button') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(host.querySelectorAll('[data-testid="toast"]').length).toBe(1);
    });
  });

  describe('RTL', () => {
    it('translates component text after switching to Arabic', async () => {
      await TestBed.inject(I18nService).use('ar');
      const { host } = await render(StatusPill, { status: 'online' });
      expect(host.textContent?.trim()).toBe('متصل');
      expect(document.documentElement.dir).toBe('rtl');
      await TestBed.inject(I18nService).use('en');
    });
  });
});
