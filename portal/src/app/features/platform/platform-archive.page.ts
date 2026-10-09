import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { TenantsApi } from '../../core/api/api.services';
import { Paged, TenantCard } from '../../core/api/models';
import { I18nService } from '../../core/i18n/i18n.service';
import { ScopeStore } from '../../core/state/scope.store';
import { Button } from '../../shared/ui/button';
import { Card } from '../../shared/ui/card';
import { ConfirmWithReasonDialog } from '../../shared/ui/confirm-with-reason-dialog';
import { CellDef, Column, DataTable } from '../../shared/ui/data-table';
import { PageHeader } from '../../shared/ui/headers';
import { Pagination } from '../../shared/ui/pagination';
import { SearchInput } from '../../shared/ui/search-input';
import { ErrorState } from '../../shared/ui/states';
import { StatusPill } from '../../shared/ui/status-pill';

/** Plans with the archive feature (04 section 2). */
const ARCHIVE_PLANS = ['BUSINESS', 'ENTERPRISE'];

/**
 * Platform Archive: every customer with the archive feature; "Open Archive" opens the customer's workspace (with a reason,
 * audited like any workspace) on its Archive screen.
 */
@Component({
  selector: 'mc-platform-archive-page',
  imports: [PageHeader, Card, DataTable, CellDef, Button, SearchInput, Pagination, StatusPill, ErrorState, ConfirmWithReasonDialog],
  template: `
    <mc-page-header [title]="i18n.t('nav.archive')" [subtitle]="i18n.t('archive.platformSubtitle')" />
    <div class="toolbar">
      <mc-search-input [placeholder]="i18n.t('customers.search')" (searched)="search.set($event); page.set(1)" />
    </div>
    @if (failed()) {
      <mc-error-state (retry)="load()" />
    } @else {
      <mc-card [flush]="true">
        <mc-data-table [columns]="columns()" [rows]="result()?.items ?? []" [loading]="loading()" [emptyText]="i18n.t('customers.empty')">
          <ng-template mcCell="name" let-row><strong>{{ t(row).name }}</strong><small class="code">{{ t(row).code }}</small></ng-template>
          <ng-template mcCell="status" let-row><mc-status-pill [status]="t(row).status.toLowerCase()" /></ng-template>
          <ng-template mcCell="plan" let-row>{{ t(row).planName ?? i18n.t('customers.planPending') }}</ng-template>
          <ng-template mcCell="city" let-row>{{ t(row).city }}, {{ t(row).country }}</ng-template>
          <ng-template mcCell="actions" let-row>
            @if (hasArchive(t(row))) {
              <button type="button" mcButton="primary-outline" size="sm" (click)="target.set(t(row))" [attr.data-testid]="'open-archive-' + t(row).code">{{ i18n.t('archive.open') }}</button>
            } @else {
              <small class="muted">{{ i18n.t('settings.notInPlan') }}</small>
            }
          </ng-template>
        </mc-data-table>
      </mc-card>
      @if (result(); as r) {
        <mc-pagination [page]="r.page" [pageSize]="r.pageSize" [total]="r.total" (pageChange)="page.set($event)" />
      }
    }
    <mc-confirm-reason-dialog [open]="target() !== null" (openChange)="!$event && target.set(null)" [title]="i18n.t('customers.dialog.workspace', { name: target()?.name ?? '' })"
      [message]="i18n.t('customers.workspaceReason')" [confirmLabel]="i18n.t('archive.open')" [busy]="busy()" (confirmed)="open($event)" />
  `,
  styles: `
    :host { display: block; }
    .toolbar { display: flex; gap: var(--mc-space-3); margin-block-end: var(--mc-space-4); }
    .toolbar mc-search-input { flex: 1; max-inline-size: 420px; }
    .code { display: block; color: var(--mc-text-muted); font-size: var(--mc-fs-xs); }
    .muted { color: var(--mc-text-muted); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlatformArchivePage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(TenantsApi);
  private readonly scope = inject(ScopeStore);
  private readonly router = inject(Router);

  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly result = signal<Paged<TenantCard> | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly target = signal<TenantCard | null>(null);
  protected readonly busy = signal(false);

  protected readonly columns = computed<Column[]>(() => [
    { key: 'name', label: this.i18n.t('customers.customer') },
    { key: 'status', label: this.i18n.t('common.status') },
    { key: 'plan', label: this.i18n.t('customers.plan') },
    { key: 'city', label: this.i18n.t('customers.city') },
    { key: 'actions', label: '', width: '170px' },
  ]);

  constructor() {
    effect(() => {
      this.search();
      this.page();
      untracked(() => void this.load());
    });
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);
    try {
      this.result.set(await firstValueFrom(this.api.list({ search: this.search() || null, sort: 'name', page: this.page(), pageSize: 25 })));
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected t(row: unknown): TenantCard {
    return row as TenantCard;
  }

  protected hasArchive(tenant: TenantCard): boolean {
    return tenant.status !== 'Archived' && ARCHIVE_PLANS.includes(tenant.planCode ?? '');
  }

  protected async open(reason: string): Promise<void> {
    const tenant = this.target();
    if (!tenant) {
      return;
    }
    this.busy.set(true);
    try {
      await firstValueFrom(this.api.openWorkspace(tenant.id, reason));
      this.scope.enterWorkspace({ id: tenant.id, name: tenant.name });
      this.target.set(null);
      await this.router.navigate(['/admin/customers', tenant.id, 'archive']);
    } catch {
      // The error toast explains why.
    } finally {
      this.busy.set(false);
    }
  }
}
