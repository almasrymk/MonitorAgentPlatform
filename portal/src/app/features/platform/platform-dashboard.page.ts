import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { debounceTime, firstValueFrom } from 'rxjs';

import { DashboardsApi } from '../../core/api/api.services';
import type { components } from '../../core/api/schema';
import { PlatformDashboard } from '../../core/api/models';
import { I18nService } from '../../core/i18n/i18n.service';
import { LiveService } from '../../core/live/live.service';
import { Button } from '../../shared/ui/button';
import { Card } from '../../shared/ui/card';
import { CellDef, Column, DataTable } from '../../shared/ui/data-table';
import { DonutChart, DonutSegment } from '../../shared/ui/donut-chart';
import { HBar, HBarItem } from '../../shared/ui/hbar';
import { PageHeader } from '../../shared/ui/headers';
import { HealthBar } from '../../shared/ui/health-bar';
import { KpiTile } from '../../shared/ui/kpi-tile';
import { Skeleton } from '../../shared/ui/skeleton';
import { ErrorState } from '../../shared/ui/states';
import { StatusPill } from '../../shared/ui/status-pill';
import { Timeline, TimelineItem } from '../../shared/ui/accordion';
import { DaysSelect, IncidentTrend, RecentAlerts } from '../alerts/alert-widgets';
import { tileViews } from '../dashboard/tiles';

type TopCustomer = components['schemas']['TopCustomerDto'];
type Expiring = components['schemas']['ExpiringSubscriptionDto'];

const OS_COLORS: Record<string, string> = { Windows: 'mc-series-windows', Linux: 'mc-series-linux', MacOS: 'mc-series-macos', Other: 'mc-series-other' };
const PLAN_COLORS: Record<string, string> = { Enterprise: 'mc-series-enterprise', Business: 'mc-series-business', Professional: 'mc-series-professional', Starter: 'mc-series-starter' };
const ACTIVITY_TONES: Record<string, TimelineItem['tone']> = { device: 'success', auth: 'info', tenant: 'warning', user: 'info' };

/** Platform Admin Dashboard (07 section 5.1). */
@Component({
  selector: 'mc-platform-dashboard-page',
  imports: [PageHeader, KpiTile, Card, DonutChart, DataTable, CellDef, HealthBar, HBar, StatusPill, Timeline, Button, Skeleton, ErrorState, IncidentTrend, DaysSelect, RecentAlerts],
  template: `
    <mc-page-header [title]="i18n.t('platformDashboard.title')" [subtitle]="i18n.t('platformDashboard.subtitle')" />
    @if (failed()) {
      <mc-error-state (retry)="load()" />
    } @else if (data(); as d) {
      <section class="tiles tiles-8" data-testid="platform-tiles">
        @for (t of tiles(); track t.key) {
          <mc-kpi-tile [attr.data-testid]="'tile-' + t.key" [icon]="t.icon" [tone]="t.tone" [label]="t.label" [value]="t.value" [delta]="t.delta" [deltaPercent]="t.deltaPercent" [caption]="t.caption" [goodWhen]="t.goodWhen" />
        }
      </section>

      <section class="row">
        <mc-card [title]="i18n.t('dashboard.incidentTrend')">
          <mc-days-select cardActions [value]="trendDays()" (valueChange)="setTrendDays($event)" />
          <mc-incident-trend [trend]="d.incidentTrend" />
        </mc-card>
        <mc-card [title]="i18n.t('platformDashboard.bySeverity')" data-testid="incidents-by-severity">
          <span cardActions class="muted">{{ i18n.t('dashboard.lastDays', { n: 30 }) }}</span>
          <mc-donut-chart [segments]="severity()" [centerLabel]="i18n.t('platformDashboard.incidents')" />
          <p class="muted center" data-testid="resolved">{{ i18n.t('platformDashboard.resolved', { n: d.resolvedIncidents }) }}</p>
        </mc-card>
        <mc-card [title]="i18n.t('platformDashboard.subscriptions')">
          <mc-donut-chart [segments]="plans()" [centerLabel]="i18n.t('platformDashboard.customers')" data-testid="subscription-distribution" />
        </mc-card>
      </section>

      <section class="row">
        <mc-card [title]="i18n.t('platformDashboard.topCustomers')" [flush]="true">
          <button cardActions type="button" mcButton="ghost" size="sm" (click)="go('customers')">{{ i18n.t('common.viewAll') }}</button>
          <mc-data-table [columns]="customerColumns()" [rows]="indexed(d.topCustomers)" data-testid="top-customers">
            <ng-template mcCell="rank" let-row>{{ c(row).rank }}</ng-template>
            <ng-template mcCell="name" let-row><strong>{{ c(row).name }}</strong></ng-template>
            <ng-template mcCell="healthScore" let-row><mc-health-bar [value]="c(row).healthScore" [label]="i18n.t('customers.healthScore')" /></ng-template>
          </mc-data-table>
        </mc-card>
        <mc-card [title]="i18n.t('platformDashboard.expiring')" [flush]="true">
          <button cardActions type="button" mcButton="ghost" size="sm" (click)="go('customers')">{{ i18n.t('common.viewAll') }}</button>
          <mc-data-table [columns]="expiringColumns()" [rows]="d.expiringSubscriptions" [emptyText]="i18n.t('platformDashboard.noneExpiring')">
            <ng-template mcCell="name" let-row><strong>{{ e(row).name }}</strong></ng-template>
            <ng-template mcCell="planName" let-row>{{ e(row).planName ?? '—' }}</ng-template>
            <ng-template mcCell="daysLeft" let-row>{{ i18n.t('platformDashboard.days', { n: e(row).daysLeft }) }}</ng-template>
            <ng-template mcCell="status" let-row><mc-status-pill [status]="e(row).status" /></ng-template>
          </mc-data-table>
        </mc-card>
        <mc-card [title]="i18n.t('dashboard.recentAlerts')" [flush]="true">
          <button cardActions type="button" mcButton="ghost" size="sm" (click)="go('notifications')">{{ i18n.t('common.viewAll') }}</button>
          <mc-recent-alerts [alerts]="d.recentAlerts" mode="platform" />
        </mc-card>
      </section>

      <section class="row">
        <mc-card [title]="i18n.t('platformDashboard.deviceHealth')">
          <mc-donut-chart [segments]="health()" [centerLabel]="i18n.t('dashboard.devicesCaption')" data-testid="device-health" />
        </mc-card>
        <mc-card [title]="i18n.t('dashboard.devicesByOs')">
          <mc-hbar [items]="os()" [ariaLabel]="i18n.t('dashboard.devicesByOs')" data-testid="devices-by-os" />
        </mc-card>
        <mc-card [title]="i18n.t('platformDashboard.activity')">
          <mc-timeline [items]="activity()" data-testid="recent-activity" />
        </mc-card>
      </section>
    } @else {
      <section class="tiles tiles-8">
        @for (i of [1, 2, 3, 4, 5, 6, 7, 8]; track i) {
          <mc-skeleton [height]="96" />
        }
      </section>
      <section class="row"><mc-skeleton [height]="240" /><mc-skeleton [height]="240" /><mc-skeleton [height]="240" /></section>
    }
  `,
  styleUrl: '../dashboard/dashboard.scss',
  styles: `.center { text-align: center; margin-block-start: var(--mc-space-2); }`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlatformDashboardPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(DashboardsApi);
  private readonly router = inject(Router);

  protected readonly data = signal<PlatformDashboard | null>(null);
  protected readonly failed = signal(false);
  protected readonly trendDays = signal('7');
  protected readonly tiles = computed(() => tileViews(this.i18n, this.data()?.tiles ?? []));

  protected readonly severity = computed<DonutSegment[]>(() => {
    const colors: Record<string, string> = { Critical: 'mc-danger', Warning: 'mc-warning', Info: 'mc-info' };
    return (this.data()?.incidentsBySeverity ?? []).map((s) => ({ key: s.name, label: this.i18n.t(`severity.${s.name.toLowerCase()}`), value: s.count, color: colors[s.name] ?? 'mc-neutral' }));
  });

  protected readonly plans = computed<DonutSegment[]>(() =>
    (this.data()?.subscriptionDistribution ?? []).map((p) => ({ key: p.name, label: p.name, value: p.count, color: PLAN_COLORS[p.name] ?? 'mc-neutral' })),
  );

  protected readonly health = computed<DonutSegment[]>(() => {
    const h = this.data()?.deviceHealth;
    return h
      ? [
          { key: 'healthy', label: this.i18n.t('status.healthy'), value: h.healthy, color: 'mc-success' },
          { key: 'warning', label: this.i18n.t('status.warning'), value: h.warning, color: 'mc-warning' },
          { key: 'critical', label: this.i18n.t('status.critical'), value: h.critical, color: 'mc-danger' },
          { key: 'offline', label: this.i18n.t('status.offline'), value: h.offline, color: 'mc-neutral' },
        ]
      : [];
  });

  protected readonly os = computed<HBarItem[]>(() =>
    (this.data()?.devicesByOs ?? []).map((o) => ({ key: o.name, label: this.i18n.t(`os.${o.name.toLowerCase()}`), value: o.count, percent: o.percent, color: OS_COLORS[o.name] ?? 'mc-series-other' })),
  );

  protected readonly activity = computed<TimelineItem[]>(() =>
    (this.data()?.recentActivity ?? []).map((a) => ({
      at: new Date(a.at).toLocaleString(this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB', { dateStyle: 'medium', timeStyle: 'short' }),
      title: this.i18n.has(`activity.${a.action}`) ? this.i18n.t(`activity.${a.action}`) : a.action,
      description: [a.details, a.actorName].filter((p) => !!p).join(' · '),
      tone: ACTIVITY_TONES[a.action.split('.')[0]] ?? 'neutral',
    })),
  );

  protected readonly customerColumns = computed<Column[]>(() => [
    { key: 'rank', label: '#', width: '36px' },
    { key: 'name', label: this.i18n.t('alerts.customer') },
    { key: 'devices', label: this.i18n.t('nav.devices') },
    { key: 'online', label: this.i18n.t('status.online') },
    { key: 'healthScore', label: this.i18n.t('customers.healthScore') },
  ]);

  protected readonly expiringColumns = computed<Column[]>(() => [
    { key: 'name', label: this.i18n.t('alerts.customer') },
    { key: 'planName', label: this.i18n.t('customers.plan') },
    { key: 'daysLeft', label: this.i18n.t('platformDashboard.expiresIn') },
    { key: 'status', label: this.i18n.t('common.status') },
  ]);

  constructor() {
    void this.load();
    const live = inject(LiveService);
    live.watch({ kind: 'platform' }, inject(DestroyRef));
    live.summary$.pipe(takeUntilDestroyed()).subscribe((event) => {
      if (event.scope === 'platform') {
        void this.load(true);
      }
    });
    live.alert$.pipe(debounceTime(1500), takeUntilDestroyed()).subscribe(() => void this.load(true));
  }

  async load(quiet = false): Promise<void> {
    if (!quiet) {
      this.failed.set(false);
    }
    try {
      this.data.set(await firstValueFrom(this.api.platform(Number(this.trendDays()))));
    } catch {
      if (!quiet || !this.data()) {
        this.failed.set(true);
      }
    }
  }

  protected setTrendDays(days: string): void {
    this.trendDays.set(days);
    void this.load(true);
  }

  protected indexed(rows: TopCustomer[]): (TopCustomer & { id: string; rank: number })[] {
    return rows.map((r, i) => ({ ...r, id: r.tenantId, rank: i + 1 }));
  }

  protected c(row: unknown): TopCustomer & { rank: number } {
    return row as TopCustomer & { rank: number };
  }

  protected e(row: unknown): Expiring {
    return row as Expiring;
  }

  protected go(path: string): void {
    void this.router.navigate(['/admin', path]);
  }
}
