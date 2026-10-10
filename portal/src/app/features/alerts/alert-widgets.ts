import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, model } from '@angular/core';

import type { components } from '../../core/api/schema';
import { I18nService } from '../../core/i18n/i18n.service';
import { CellDef, Column, DataTable } from '../../shared/ui/data-table';
import { Select } from '../../shared/ui/select';
import { EmptyState } from '../../shared/ui/states';
import { StatusPill } from '../../shared/ui/status-pill';
import { TrendChart, TrendSeries } from '../../shared/ui/trend-chart';

type TrendSeriesDto = components['schemas']['TrendSeriesDto'];
type RecentAlertDto = components['schemas']['RecentAlertDto'];

const SEVERITY_COLORS: Record<string, string> = { Critical: 'mc-danger', Warning: 'mc-warning', Info: 'mc-info' };

/** Incident Trend: alerts opened per day by severity (07 sections 5.1, 5.3, 5.5). */
@Component({
  selector: 'mc-incident-trend',
  imports: [TrendChart, EmptyState],
  template: `
    @if (empty()) {
      <mc-empty-state icon="reports" [title]="i18n.t('dashboard.noIncidents')" />
    } @else {
      <mc-trend-chart [series]="series()" unit="" [max]="null" [height]="height()" data-testid="incident-trend" />
    }
    <ul class="legend">
      @for (s of series(); track s.name) {
        <li><span class="swatch" [style.background]="'var(--' + s.color + ')'"></span>{{ s.name }}</li>
      }
    </ul>
  `,
  styles: `
    .legend { display: flex; gap: var(--mc-space-4); justify-content: center; list-style: none; margin: var(--mc-space-2) 0 0; padding: 0; font-size: var(--mc-fs-xs); color: var(--mc-text-secondary); }
    .swatch { display: inline-block; inline-size: 8px; block-size: 8px; border-radius: 50%; margin-inline-end: 6px; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class IncidentTrend {
  protected readonly i18n = inject(I18nService);
  readonly trend = input.required<TrendSeriesDto[]>();
  readonly height = input(180);

  readonly series = computed<TrendSeries[]>(() =>
    this.trend().map((s) => ({
      name: this.i18n.t(`severity.${s.name.toLowerCase()}`),
      color: SEVERITY_COLORS[s.name] ?? 'mc-neutral',
      points: s.points.map((p) => ({ at: p.day, value: p.value })),
    })),
  );

  protected readonly empty = computed(() => this.trend().every((s) => s.points.length === 0));
}

/** "Last 7 days" / "Last 30 days" selector shown in a widget header (07 section 1, correction 4). */
@Component({
  selector: 'mc-days-select',
  imports: [Select],
  template: `<mc-select [label]="i18n.t('dashboard.period')" [options]="options()" [value]="value()" (valueChange)="value.set($event)" />`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DaysSelect {
  protected readonly i18n = inject(I18nService);
  readonly days = input<number[]>([7, 30]);
  readonly value = model('7');
  protected readonly options = computed(() => this.days().map((d) => ({ value: String(d), label: this.i18n.t('dashboard.lastDays', { n: d }) })));
}

export type RecentAlertsMode = 'platform' | 'tenant' | 'location';

/** Recent Alerts table of the dashboards: Time, Severity, Customer or Device, Message (and Location). */
@Component({
  selector: 'mc-recent-alerts',
  imports: [DatePipe, DataTable, CellDef, StatusPill],
  template: `
    <mc-data-table [compact]="true" [columns]="columns()" [rows]="alerts()" [emptyText]="i18n.t('dashboard.noAlerts')" data-testid="recent-alerts">
      <ng-template mcCell="at" let-row><span class="time" [attr.title]="r(row).at">{{ r(row).at | date: (today(r(row).at) ? 'shortTime' : 'd MMM, HH:mm') : undefined : locale() }}</span></ng-template>
      <ng-template mcCell="severity" let-row><mc-status-pill [status]="r(row).severity" [label]="i18n.t('severity.' + r(row).severity.toLowerCase())" /></ng-template>
      <ng-template mcCell="customerName" let-row>{{ r(row).customerName ?? '—' }}</ng-template>
      <ng-template mcCell="deviceName" let-row><strong>{{ r(row).deviceName ?? '—' }}</strong></ng-template>
      <ng-template mcCell="message" let-row>{{ r(row).message }}</ng-template>
      <ng-template mcCell="locationName" let-row>{{ r(row).locationName ?? '—' }}</ng-template>
    </mc-data-table>
  `,
  styles: `.time { white-space: nowrap; color: var(--mc-text-secondary); }`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RecentAlerts {
  protected readonly i18n = inject(I18nService);
  readonly alerts = input.required<RecentAlertDto[]>();
  readonly mode = input<RecentAlertsMode>('tenant');
  protected readonly locale = computed(() => (this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB'));

  /** Recent alerts of today show the time only (as in the designs); older ones the day as well. */
  protected today(at: string): boolean {
    return new Date(at).toDateString() === new Date().toDateString();
  }

  protected r(row: unknown): RecentAlertDto {
    return row as RecentAlertDto;
  }

  protected readonly columns = computed<Column[]>(() => {
    const columns: Column[] = [
      { key: 'at', label: this.i18n.t('alerts.time') },
      { key: 'severity', label: this.i18n.t('dashboard.severity') },
      this.mode() === 'platform' ? { key: 'customerName', label: this.i18n.t('alerts.customer') } : { key: 'deviceName', label: this.i18n.t('alerts.device') },
      { key: 'message', label: this.i18n.t('alerts.message') },
    ];
    if (this.mode() === 'location') {
      columns.push({ key: 'locationName', label: this.i18n.t('dashboard.location') });
    }
    return columns;
  });
}
