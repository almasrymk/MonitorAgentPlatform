import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { TenantsApi } from '../../core/api/api.services';
import { AuditRecord, Paged, TenantCard } from '../../core/api/models';
import { AuditApi } from '../../core/api/reports.api';
import { I18nService } from '../../core/i18n/i18n.service';
import { ScopeStore } from '../../core/state/scope.store';
import { Card } from '../../shared/ui/card';
import { CellDef, Column, DataTable } from '../../shared/ui/data-table';
import { PageHeader } from '../../shared/ui/headers';
import { Pagination } from '../../shared/ui/pagination';
import { SearchInput } from '../../shared/ui/search-input';
import { ErrorState } from '../../shared/ui/states';
import { StatusPill } from '../../shared/ui/status-pill';

const RANGES: Record<string, number> = { '24h': 1, '7d': 7, '30d': 30, '90d': 90 };

/**
 * Audit log (06: /audit, /platform/audit): who did what and when. Platform staff outside a workspace see every customer
 * (with a Customer column and filter); inside a workspace or for a customer, the customer's own audit.
 */
@Component({
  selector: 'mc-audit-page',
  imports: [FormsModule, PageHeader, Card, DataTable, CellDef, Pagination, SearchInput, StatusPill, ErrorState],
  template: `
    <mc-page-header [title]="i18n.t('audit.title')" [subtitle]="i18n.t('audit.subtitle')" />
    <div class="toolbar">
      <mc-search-input [placeholder]="i18n.t('audit.searchActor')" (searched)="actor.set($event); page.set(1)" />
      <input class="action" type="search" [placeholder]="i18n.t('audit.action')" [attr.aria-label]="i18n.t('audit.action')" [ngModel]="action()" (change)="action.set($any($event.target).value); page.set(1)" data-testid="audit-action" />
      @if (platformScope()) {
        <select [ngModel]="tenantId()" (ngModelChange)="tenantId.set($event); page.set(1)" [attr.aria-label]="i18n.t('customers.customer')" data-testid="audit-tenant">
          <option value="">{{ i18n.t('audit.allCustomers') }}</option>
          @for (t of tenants(); track t.id) {
            <option [value]="t.id">{{ t.name }}</option>
          }
        </select>
      }
      <select [ngModel]="range()" (ngModelChange)="range.set($event); page.set(1)" [attr.aria-label]="i18n.t('reports.timeRange')">
        @for (r of ranges; track r) {
          <option [value]="r">{{ i18n.t('range.' + r) }}</option>
        }
      </select>
    </div>
    @if (failed()) {
      <mc-error-state (retry)="load()" />
    } @else {
      <mc-card [flush]="true">
        <mc-data-table [columns]="columns()" [rows]="result()?.items ?? []" [loading]="loading()" [emptyText]="i18n.t('audit.empty')">
          <ng-template mcCell="at" let-row>{{ date(a(row).at) }}</ng-template>
          <ng-template mcCell="customer" let-row>{{ customerName(a(row).tenantId) }}</ng-template>
          <ng-template mcCell="actor" let-row><strong>{{ a(row).actorName }}</strong><small class="sub">{{ a(row).actorType }}</small></ng-template>
          <ng-template mcCell="action" let-row><code>{{ a(row).action }}</code></ng-template>
          <ng-template mcCell="entity" let-row>{{ a(row).entityType }}</ng-template>
          <ng-template mcCell="details" let-row><span class="details" [attr.title]="a(row).details ?? ''">{{ a(row).details ?? '—' }}</span></ng-template>
          <ng-template mcCell="success" let-row><mc-status-pill [status]="a(row).success ? 'active' : 'critical'" [label]="a(row).success ? i18n.t('audit.ok') : i18n.t('audit.failed')" /></ng-template>
          <ng-template mcCell="ip" let-row>{{ a(row).ip ?? '—' }}</ng-template>
        </mc-data-table>
      </mc-card>
      @if (result(); as r) {
        <mc-pagination [page]="r.page" [pageSize]="r.pageSize" [total]="r.total" (pageChange)="page.set($event)" />
      }
    }
  `,
  styles: `
    :host { display: block; }
    .toolbar { display: flex; flex-wrap: wrap; gap: var(--mc-space-3); margin-block-end: var(--mc-space-4); }
    .toolbar mc-search-input { flex: 1; max-inline-size: 320px; }
    .toolbar select, .toolbar .action { block-size: 36px; background: var(--mc-bg-input); border: 1px solid var(--mc-border); border-radius: var(--mc-radius-md); color: var(--mc-text); font: inherit; padding-inline: var(--mc-space-2); }
    .sub { display: block; color: var(--mc-text-muted); font-size: var(--mc-fs-xs); }
    .details { display: inline-block; max-inline-size: 320px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; vertical-align: bottom; }
    code { font-size: var(--mc-fs-sm); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(AuditApi);
  private readonly tenantsApi = inject(TenantsApi);
  private readonly scope = inject(ScopeStore);

  protected readonly ranges = Object.keys(RANGES);
  protected readonly actor = signal('');
  protected readonly action = signal('');
  protected readonly tenantId = signal(inject(ActivatedRoute).snapshot.queryParamMap.get('tenantId') ?? '');
  protected readonly range = signal('30d');
  protected readonly page = signal(1);
  protected readonly result = signal<Paged<AuditRecord> | null>(null);
  protected readonly tenants = signal<TenantCard[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);

  /** The platform audit: a platform user outside a workspace. */
  protected readonly platformScope = signal(this.scope.workspace() === null && inject(ActivatedRoute).snapshot.data['platform'] === true);

  protected readonly columns = computed<Column[]>(() => [
    { key: 'at', label: this.i18n.t('alerts.time') },
    ...(this.platformScope() ? [{ key: 'customer', label: this.i18n.t('customers.customer') }] : []),
    { key: 'actor', label: this.i18n.t('audit.actor') },
    { key: 'action', label: this.i18n.t('audit.action') },
    { key: 'entity', label: this.i18n.t('audit.entity') },
    { key: 'details', label: this.i18n.t('audit.details') },
    { key: 'success', label: this.i18n.t('common.status') },
    { key: 'ip', label: 'IP' },
  ]);

  constructor() {
    if (this.platformScope()) {
      void firstValueFrom(this.tenantsApi.list({ pageSize: 200, sort: 'name' })).then((r) => this.tenants.set(r.items)).catch(() => undefined);
    }
    effect(() => {
      this.actor();
      this.action();
      this.tenantId();
      this.range();
      this.page();
      untracked(() => void this.load());
    });
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);
    const to = new Date();
    const from = new Date(to.getTime() - RANGES[this.range()] * 86_400_000);
    const q = { actor: this.actor() || null, action: this.action() || null, from: from.toISOString(), to: to.toISOString(), page: this.page(), pageSize: 25 };
    try {
      this.result.set(await firstValueFrom(this.platformScope() ? this.api.platform({ ...q, tenantId: this.tenantId() || null }) : this.api.tenant(q)));
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected a(row: unknown): AuditRecord {
    return row as AuditRecord;
  }

  protected customerName(id: string | null): string {
    return id ? (this.tenants().find((t) => t.id === id)?.name ?? '—') : this.i18n.t('nav.platform');
  }

  protected date(at: string): string {
    return new Date(at).toLocaleString(this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB', { dateStyle: 'medium', timeStyle: 'medium' });
  }
}
