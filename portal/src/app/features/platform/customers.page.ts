import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { TenantsApi } from '../../core/api/api.services';
import { Paged, TenantCard, TenantsSummary } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { ScopeStore } from '../../core/state/scope.store';
import { ToastService } from '../../core/ui/toast.service';
import { Button } from '../../shared/ui/button';
import { ConfirmWithReasonDialog } from '../../shared/ui/confirm-with-reason-dialog';
import { CellDef, Column, DataTable } from '../../shared/ui/data-table';
import { PageHeader } from '../../shared/ui/headers';
import { KpiTile } from '../../shared/ui/kpi-tile';
import { Pagination } from '../../shared/ui/pagination';
import { SearchInput } from '../../shared/ui/search-input';
import { Select } from '../../shared/ui/select';
import { Skeleton } from '../../shared/ui/skeleton';
import { EmptyState, ErrorState } from '../../shared/ui/states';
import { StatusPill } from '../../shared/ui/status-pill';
import { CustomerAction, CustomerCard } from '../../shared/ui/customer-card';
import { HealthBar } from '../../shared/ui/health-bar';
import { ViewToggle } from '../../shared/ui/view-toggle';

interface Action {
  kind: 'workspace' | 'suspend' | 'archive';
  tenant: TenantCard;
}

/** Customers (07 section 5.2): customer cards with plan, devices, health, licence usage and renewal. */
@Component({
  selector: 'mc-customers-page',
  imports: [PageHeader, KpiTile, SearchInput, Select, StatusPill, Button, Skeleton, Pagination, ConfirmWithReasonDialog, DataTable, CellDef, EmptyState, ErrorState, CustomerCard, HealthBar, ViewToggle],
  templateUrl: './customers.page.html',
  styleUrl: './customers.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CustomersPage {
  protected readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthService);
  private readonly api = inject(TenantsApi);
  private readonly scope = inject(ScopeStore);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  protected readonly pageSize = 24;
  protected readonly search = signal('');
  protected readonly status = signal('');
  protected readonly plan = signal('');
  protected readonly health = signal('');
  protected readonly sort = signal('name');
  protected readonly page = signal(1);
  protected readonly view = signal<'grid' | 'list'>('grid');

  protected readonly summary = signal<TenantsSummary | null>(null);
  protected readonly result = signal<Paged<TenantCard> | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly action = signal<Action | null>(null);
  protected readonly busy = signal(false);
  protected readonly canManage = computed(() => this.auth.hasPermission('platform.tenants.manage'));

  protected readonly statusOptions = computed(() => [
    { value: '', label: this.i18n.t('customers.allStatuses') },
    { value: 'Active', label: this.i18n.t('status.active') },
    { value: 'Trial', label: this.i18n.t('status.trial') },
    { value: 'Expiring', label: this.i18n.t('status.expiring') },
    { value: 'Suspended', label: this.i18n.t('status.suspended') },
    { value: 'Expired', label: this.i18n.t('status.expired') },
  ]);
  protected readonly planOptions = computed(() => [
    { value: '', label: this.i18n.t('customers.allPlans') },
    ...['ENTERPRISE', 'BUSINESS', 'PROFESSIONAL', 'STARTER'].map((code) => ({ value: code, label: this.i18n.t(`plan.${code}`) })),
  ]);
  protected readonly healthOptions = computed(() => [
    { value: '', label: this.i18n.t('customers.allHealth') },
    ...['healthy', 'warning', 'critical'].map((h) => ({ value: h, label: this.i18n.t(`status.${h}`) })),
  ]);
  protected readonly sortOptions = computed(() => [
    { value: 'name', label: this.i18n.t('customers.sortName') },
    { value: '-customerSince', label: this.i18n.t('customers.sortNewest') },
    { value: 'customerSince', label: this.i18n.t('customers.sortOldest') },
  ]);
  protected readonly columns = computed<Column[]>(() => [
    { key: 'name', label: this.i18n.t('customers.customer'), sortable: true },
    { key: 'status', label: this.i18n.t('common.status'), sortable: true },
    { key: 'plan', label: this.i18n.t('customers.plan') },
    { key: 'locations', label: this.i18n.t('customers.locations') },
    { key: 'devices', label: this.i18n.t('customers.devices') },
    { key: 'health', label: this.i18n.t('customers.healthScore') },
    { key: 'city', label: this.i18n.t('customers.city') },
    { key: 'actions', label: '' },
  ]);

  protected readonly dialogOpen = computed(() => this.action() !== null);

  constructor() {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);
    try {
      const [summary, result] = await Promise.all([
        firstValueFrom(this.api.summary()),
        firstValueFrom(this.api.list({ search: this.search(), plan: this.plan(), health: this.health(), subscriptionStatus: this.status(), sort: this.sort(), page: this.page(), pageSize: this.pageSize })),
      ]);
      this.summary.set(summary);
      this.result.set(result);
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected setFilter(apply: () => void): void {
    apply();
    this.page.set(1);
    void this.load();
  }

  protected goTo(page: number): void {
    this.page.set(page);
    void this.load();
  }

  protected ask(kind: Action['kind'], tenant: TenantCard): void {
    this.action.set({ kind, tenant });
  }

  protected onCard(action: CustomerAction, tenant: TenantCard): void {
    if (action === 'reactivate') {
      void this.resume(tenant);
    } else {
      this.ask(action, tenant);
    }
  }

  protected async resume(tenant: TenantCard): Promise<void> {
    await firstValueFrom(this.api.resume(tenant.id));
    this.toast.success(this.i18n.t('customers.resumed', { name: tenant.name }));
    await this.load();
  }

  protected closeDialog(open: boolean): void {
    if (!open) {
      this.action.set(null);
    }
  }

  async confirm(reason: string): Promise<void> {
    const action = this.action();
    if (!action) {
      return;
    }
    this.busy.set(true);
    try {
      if (action.kind === 'workspace') {
        await firstValueFrom(this.api.openWorkspace(action.tenant.id, reason));
        this.scope.enterWorkspace({ id: action.tenant.id, name: action.tenant.name });
        this.action.set(null);
        await this.router.navigate(['/admin/customers', action.tenant.id, 'overview']);
        return;
      }
      if (action.kind === 'suspend') {
        await firstValueFrom(this.api.suspend(action.tenant.id, reason));
        this.toast.success(this.i18n.t('customers.suspended', { name: action.tenant.name }));
      } else {
        await firstValueFrom(this.api.archive(action.tenant.id, reason));
        this.toast.success(this.i18n.t('customers.archived', { name: action.tenant.name }));
      }
      this.action.set(null);
      await this.load();
    } catch {
      // The error interceptor shows the problem.
    } finally {
      this.busy.set(false);
    }
  }

  protected dialogTitle(): string {
    const action = this.action();
    if (!action) {
      return '';
    }
    return this.i18n.t(`customers.dialog.${action.kind}`, { name: action.tenant.name });
  }

  /** Suspended customer, else "Expiring" when the renewal is within 30 days, else the subscription status. */
  protected pill(card: TenantCard): string {
    if (card.status !== 'Active') {
      return card.status;
    }
    if (card.expiringSoon) {
      return 'expiring';
    }
    return card.subscriptionStatus && card.subscriptionStatus !== 'None' ? card.subscriptionStatus : card.status;
  }
}
