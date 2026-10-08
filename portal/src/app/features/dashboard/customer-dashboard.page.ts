import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { debounceTime, firstValueFrom } from 'rxjs';

import { DashboardsApi } from '../../core/api/api.services';
import { LocationStatusRow, ProblemDevice, TenantDashboard } from '../../core/api/models';
import { I18nService } from '../../core/i18n/i18n.service';
import { areaRoot } from '../../core/layout/area';
import { LiveService } from '../../core/live/live.service';
import { ScopeStore } from '../../core/state/scope.store';
import { Button } from '../../shared/ui/button';
import { Card } from '../../shared/ui/card';
import { CellDef, Column, DataTable } from '../../shared/ui/data-table';
import { DonutChart, DonutSegment } from '../../shared/ui/donut-chart';
import { EntityHeader } from '../../shared/ui/headers';
import { HealthBar } from '../../shared/ui/health-bar';
import { KpiTile } from '../../shared/ui/kpi-tile';
import { LocationCard } from '../../shared/ui/location-card';
import { Skeleton } from '../../shared/ui/skeleton';
import { EmptyState, ErrorState } from '../../shared/ui/states';
import { StatusPill } from '../../shared/ui/status-pill';
import { UsageBar } from '../../shared/ui/usage-bar';
import { tileViews } from './tiles';
import { DaysSelect, IncidentTrend, RecentAlerts } from '../alerts/alert-widgets';

/** Customer Dashboard / Customer Workspace overview (07 section 5.3). */
@Component({
  selector: 'mc-customer-dashboard-page',
  imports: [DatePipe, EntityHeader, KpiTile, Card, LocationCard, DonutChart, DataTable, CellDef, HealthBar, StatusPill, UsageBar, Button, Skeleton, EmptyState, ErrorState, IncidentTrend, DaysSelect, RecentAlerts],
  templateUrl: './customer-dashboard.page.html',
  styleUrl: './dashboard.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CustomerDashboardPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(DashboardsApi);
  private readonly router = inject(Router);

  protected readonly data = signal<TenantDashboard | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly trendDays = signal('7');
  protected readonly root = computed(() => areaRoot(this.router.url));
  protected readonly locale = computed(() => (this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB'));

  protected readonly tiles = computed(() => tileViews(this.i18n, this.data()?.tiles ?? []));

  protected readonly meta = computed(() => {
    const h = this.data()?.header;
    if (!h) {
      return [];
    }
    const since = new Date(h.customerSince).toLocaleDateString(this.locale(), { month: 'short', year: 'numeric' });
    return [
      h.planName ?? this.i18n.t('customers.planPending'),
      this.i18n.t('dashboard.locationsCount', { n: h.locations }),
      this.i18n.t('dashboard.devicesCount', { n: h.devices }),
      this.i18n.t('dashboard.customerSince', { date: since }),
    ];
  });

  protected readonly health = computed<DonutSegment[]>(() => {
    const h = this.data()?.locationsHealth;
    return h
      ? [
          { key: 'healthy', label: this.i18n.t('status.healthy'), value: h.healthy, color: 'mc-success' },
          { key: 'warning', label: this.i18n.t('status.warning'), value: h.warning, color: 'mc-warning' },
          { key: 'critical', label: this.i18n.t('status.critical'), value: h.critical, color: 'mc-danger' },
          { key: 'offline', label: this.i18n.t('status.offline'), value: h.offline, color: 'mc-neutral' },
        ]
      : [];
  });

  protected readonly license = computed<DonutSegment[]>(() => {
    const l = this.data()?.license;
    return l
      ? [
          { key: 'licensed', label: this.i18n.t('status.licensed'), value: l.licensed, color: 'mc-info' },
          { key: 'unlicensed', label: this.i18n.t('status.unlicensed'), value: l.unlicensed, color: 'mc-neutral' },
        ]
      : [];
  });

  protected readonly problemColumns = computed<Column[]>(() => [
    { key: 'name', label: this.i18n.t('dashboard.deviceName') },
    { key: 'locationName', label: this.i18n.t('dashboard.location') },
    { key: 'issue', label: this.i18n.t('dashboard.issue') },
    { key: 'health', label: this.i18n.t('common.status') },
  ]);

  protected readonly locationColumns = computed<Column[]>(() => [
    { key: 'name', label: this.i18n.t('dashboard.location') },
    { key: 'total', label: this.i18n.t('dashboard.total') },
    { key: 'online', label: this.i18n.t('status.online') },
    { key: 'healthy', label: this.i18n.t('status.healthy') },
    { key: 'warning', label: this.i18n.t('status.warning') },
    { key: 'critical', label: this.i18n.t('status.critical') },
    { key: 'healthPercent', label: this.i18n.t('dashboard.healthPercent') },
  ]);

  constructor() {
    void this.load();
    const live = inject(LiveService);
    live.watch({ kind: 'tenant', id: inject(ScopeStore).workspace()?.id ?? null }, inject(DestroyRef));
    live.summary$.pipe(takeUntilDestroyed()).subscribe((event) => {
      if (event.scope === 'tenant') {
        void this.load(true);
      }
    });
    live.alert$.pipe(debounceTime(1500), takeUntilDestroyed()).subscribe(() => void this.load(true));
  }

  /** quiet: a live refresh keeps the current content instead of showing skeletons. */
  async load(quiet = false): Promise<void> {
    if (quiet && this.data()) {
      try {
        this.data.set(await firstValueFrom(this.api.tenant(Number(this.trendDays()))));
      } catch {
        // Keep the current numbers.
      }
      return;
    }
    this.loading.set(true);
    this.failed.set(false);
    try {
      this.data.set(await firstValueFrom(this.api.tenant(Number(this.trendDays()))));
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected problem(row: unknown): ProblemDevice {
    return row as ProblemDevice;
  }

  protected locationRow(row: unknown): LocationStatusRow {
    return row as LocationStatusRow;
  }

  protected setTrendDays(days: string): void {
    this.trendDays.set(days);
    void this.load(true);
  }

  protected openNotifications(): void {
    void this.router.navigate([this.root(), 'notifications']);
  }

  protected openLocations(): void {
    void this.router.navigate([this.root(), 'locations']);
  }
}
