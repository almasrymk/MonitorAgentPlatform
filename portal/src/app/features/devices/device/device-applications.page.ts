import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { DevicesApi } from '../../../core/api/api.services';
import { I18nService } from '../../../core/i18n/i18n.service';
import { Column, DataTable } from '../../../shared/ui/data-table';
import { SearchInput } from '../../../shared/ui/search-input';
import { EmptyState } from '../../../shared/ui/states';
import { TabItem, Tabs } from '../../../shared/ui/tabs';
import { DeviceContext } from './device-context';

type Row = Record<string, unknown> & { id: string };

/** Applications tab (07 section 5.7): Programs, Services and Users from the inventory, with search. */
@Component({
  selector: 'mc-device-applications-page',
  imports: [Tabs, SearchInput, DataTable, EmptyState],
  template: `
    <mc-tabs [tabs]="tabs()" [active]="kind()" (activeChange)="kind.set($event)" />
    <div class="toolbar"><mc-search-input [placeholder]="i18n.t('device.searchInventory')" [value]="search()" (searched)="search.set($event)" data-testid="inventory-search" /></div>
    @if (rows() === null) {
      <mc-empty-state icon="list" [title]="i18n.t('device.inventoryMissing')" />
    } @else {
      <mc-data-table [columns]="columns()" [rows]="filtered()" [emptyText]="i18n.t('device.noMatches')" data-testid="inventory-table" />
    }
  `,
  styles: `
    .toolbar { margin-block: var(--mc-space-3); max-inline-size: 420px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DeviceApplicationsPage {
  protected readonly i18n = inject(I18nService);
  private readonly context = inject(DeviceContext);
  private readonly api = inject(DevicesApi);

  protected readonly kind = signal('programs');
  protected readonly search = signal('');
  protected readonly rows = signal<Row[] | null>([]);

  protected readonly tabs = computed<TabItem[]>(() => ['programs', 'services', 'users'].map((id) => ({ id, label: this.i18n.t(`device.inventory.${id}`) })));
  protected readonly columns = computed<Column[]>(() => {
    const first = this.rows()?.[0];
    return first ? Object.keys(first).filter((k) => k !== 'id').map((key) => ({ key, label: key })) : [];
  });
  protected readonly filtered = computed(() => {
    const term = this.search().trim().toLowerCase();
    const rows = this.rows() ?? [];
    return term ? rows.filter((r) => Object.values(r).some((v) => String(v).toLowerCase().includes(term))) : rows;
  });

  constructor() {
    effect(() => {
      const id = this.context.id();
      const kind = this.kind();
      if (id) {
        void this.load(id, kind);
      }
    });
  }

  private async load(id: string, kind: string): Promise<void> {
    try {
      const doc = await firstValueFrom(this.api.inventory(id, kind));
      const items = Array.isArray(doc.document) ? doc.document : ((doc.document as { items?: unknown[] }).items ?? []);
      this.rows.set(
        (items as Record<string, unknown>[]).map((item, i) => ({
          id: String(i),
          ...Object.fromEntries(Object.entries(item).map(([k, v]) => [k, v !== null && typeof v === 'object' ? JSON.stringify(v) : v])),
        })),
      );
    } catch {
      this.rows.set(null);
    }
  }
}
