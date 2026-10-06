import { ChangeDetectionStrategy, Component, signal } from '@angular/core';

import { Avatar } from '../../shared/ui/avatar';
import { Button } from '../../shared/ui/button';
import { Card } from '../../shared/ui/card';
import { ConfirmWithReasonDialog } from '../../shared/ui/confirm-with-reason-dialog';
import { Column, DataTable } from '../../shared/ui/data-table';
import { Dialog } from '../../shared/ui/dialog';
import { Drawer } from '../../shared/ui/drawer';
import { EntityHeader, PageHeader } from '../../shared/ui/headers';
import { KpiTile } from '../../shared/ui/kpi-tile';
import { Pagination } from '../../shared/ui/pagination';
import { SearchInput } from '../../shared/ui/search-input';
import { Select } from '../../shared/ui/select';
import { Skeleton } from '../../shared/ui/skeleton';
import { EmptyState, ErrorState } from '../../shared/ui/states';
import { StatusPill } from '../../shared/ui/status-pill';
import { Tabs } from '../../shared/ui/tabs';

/** Story-like page of the design-system components (development builds only, 07 section 4). */
@Component({
  selector: 'mc-components-page',
  imports: [Avatar, Button, Card, ConfirmWithReasonDialog, DataTable, Dialog, Drawer, EntityHeader, PageHeader, KpiTile, Pagination, SearchInput, Select, Skeleton, EmptyState, ErrorState, StatusPill, Tabs],
  template: `
    <mc-page-header title="Components" subtitle="Design-system components with sample inputs." />
    <mc-entity-header title="Sample Customer" status="Active" [meta]="['Enterprise', '3 Locations', '316 Devices']">
      <button type="button" mcButton="secondary">Actions</button>
    </mc-entity-header>
    <section class="row">
      <mc-kpi-tile icon="devices" tone="info" label="Total Devices" [value]="316" [delta]="12" [deltaPercent]="3.9" caption="vs last 30 days" />
      <mc-kpi-tile icon="alert" tone="danger" label="Critical" [value]="15" [delta]="3" goodWhen="down" caption="vs last 30 days" />
      <mc-kpi-tile icon="check" tone="success" label="Healthy" [value]="247" [delta]="-2" caption="% of total" />
    </section>
    <mc-card title="Buttons and pills">
      <div class="row">
        @for (variant of variants; track variant) {
          <button type="button" [mcButton]="$any(variant)">{{ variant }}</button>
        }
      </div>
      <div class="row">
        @for (status of statuses; track status) {
          <mc-status-pill [status]="status" />
        }
        <mc-avatar name="Admin User" />
        <mc-avatar name="Acme Corporation" [letters]="1" [size]="40" />
      </div>
    </mc-card>
    <mc-card title="Inputs">
      <div class="row">
        <mc-search-input placeholder="Search customers by name, domain or contact..." />
        <mc-select label="Plan" [options]="[{ value: '', label: 'All Plans' }, { value: 'ENTERPRISE', label: 'Enterprise' }]" />
      </div>
      <mc-tabs [tabs]="[{ id: 'all', label: 'All Users', count: 8 }, { id: 'admins', label: 'Administrators', count: 2 }]" [active]="tab()" (activeChange)="tab.set($event)" />
    </mc-card>
    <mc-card title="Table" [flush]="true">
      <mc-data-table [columns]="columns" [rows]="rows" [(sort)]="sort" />
      <mc-pagination [page]="1" [pageSize]="20" [total]="45" />
    </mc-card>
    <mc-card title="States">
      <div class="row states">
        <mc-skeleton [height]="60" width="200px" />
        <mc-empty-state title="No locations yet" message="Add your first location." />
        <mc-error-state />
      </div>
    </mc-card>
    <div class="row">
      <button type="button" mcButton="primary-solid" (click)="dialog.set(true)">Dialog</button>
      <button type="button" mcButton="danger-outline" (click)="reason.set(true)">Confirm with reason</button>
      <button type="button" mcButton="secondary" (click)="drawer.set(true)">Drawer</button>
    </div>
    <mc-dialog [(open)]="dialog" title="Sample dialog"><p>Dialog content.</p></mc-dialog>
    <mc-confirm-reason-dialog [(open)]="reason" title="Suspend Sample Customer" confirmLabel="Suspend" [danger]="true" />
    <mc-drawer [(open)]="drawer" title="Add Location"><p>Drawer content.</p></mc-drawer>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--mc-space-4); }
    .row { display: flex; flex-wrap: wrap; gap: var(--mc-space-3); align-items: center; margin-block-end: var(--mc-space-3); }
    .states > * { flex: 1; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ComponentsPage {
  protected readonly variants = ['primary-solid', 'primary-outline', 'secondary', 'danger-outline', 'success-outline', 'ghost'];
  protected readonly statuses = ['online', 'offline', 'active', 'suspended', 'expiring', 'licensed', 'unlicensed'];
  protected readonly tab = signal('all');
  protected readonly sort = signal<string | null>('name');
  protected readonly dialog = signal(false);
  protected readonly reason = signal(false);
  protected readonly drawer = signal(false);
  protected readonly columns: Column[] = [
    { key: 'name', label: 'Name', sortable: true },
    { key: 'city', label: 'City', sortable: true },
  ];
  protected readonly rows = [
    { id: '1', name: 'Cairo HQ', city: 'Cairo' },
    { id: '2', name: 'Dubai Office', city: 'Dubai' },
  ];
}
