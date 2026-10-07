import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import { ProblemDevice } from '../../core/api/models';
import { I18nService } from '../../core/i18n/i18n.service';
import { relativeTime } from '../../shared/format';
import { Card } from '../../shared/ui/card';
import { CellDef, Column, DataTable } from '../../shared/ui/data-table';
import { DonutChart, DonutSegment } from '../../shared/ui/donut-chart';
import { KpiTile } from '../../shared/ui/kpi-tile';
import { RingGauge } from '../../shared/ui/ring-gauge';
import { Skeleton } from '../../shared/ui/skeleton';
import { EmptyState } from '../../shared/ui/states';
import { StatusPill } from '../../shared/ui/status-pill';
import { tileViews } from '../dashboard/tiles';
import { LocationContext } from './location-context';

const OS_COLORS: Record<string, string> = { Windows: 'mc-series-windows', Linux: 'mc-series-linux', MacOS: 'mc-series-macos', Other: 'mc-series-other' };

/** Location Overview (07 section 5.5). Incident and alert blocks stay empty until M6. */
@Component({
  selector: 'mc-location-overview-page',
  imports: [DatePipe, KpiTile, Card, DonutChart, RingGauge, DataTable, CellDef, StatusPill, Skeleton, EmptyState],
  template: `
    @if (context.dashboard(); as d) {
      <section class="tiles tiles-6" data-testid="location-tiles">
        @for (t of tiles(); track t.key) {
          <mc-kpi-tile [attr.data-testid]="'tile-' + t.key" [icon]="t.icon" [tone]="t.tone" [label]="t.label" [value]="t.value" [caption]="t.caption" [goodWhen]="t.goodWhen" />
        }
      </section>

      <section class="row">
        <mc-card [title]="i18n.t('dashboard.incidentTrend')">
          <mc-empty-state icon="reports" [title]="i18n.t('dashboard.noIncidents')" [message]="i18n.t('dashboard.alertsSoon')" />
        </mc-card>
        <mc-card [title]="i18n.t('dashboard.devicesByOs')">
          <mc-donut-chart [segments]="os()" [centerLabel]="i18n.t('dashboard.devicesCaption')" data-testid="devices-by-os" />
        </mc-card>
        <mc-card [title]="i18n.t('dashboard.deviceHealth')">
          <mc-donut-chart [segments]="health()" [centerLabel]="i18n.t('dashboard.devicesCaption')" data-testid="device-health" />
        </mc-card>
      </section>

      <section class="row">
        <mc-card [title]="i18n.t('dashboard.resourceAverages')" data-testid="resources">
          <div class="rings">
            <mc-ring-gauge [value]="d.resources.cpu" tone="cpu" [label]="i18n.t('dashboard.cpuUsage')" />
            <mc-ring-gauge [value]="d.resources.ram" tone="ram" [label]="i18n.t('dashboard.ramUsage')" />
            <mc-ring-gauge [value]="d.resources.disk" tone="disk" [label]="i18n.t('dashboard.diskUsage')" />
            <mc-ring-gauge [value]="d.resources.healthScore" tone="success" [label]="i18n.t('customers.healthScore')" />
          </div>
          <p class="muted">{{ i18n.t('dashboard.acrossOnline', { n: d.resources.onlineDevices }) }}</p>
        </mc-card>
        <mc-card [title]="i18n.t('dashboard.topProblematic')" [flush]="true" class="span-2">
          <mc-data-table [columns]="problemColumns()" [rows]="d.topProblematicDevices" [emptyText]="i18n.t('dashboard.noProblems')">
            <ng-template mcCell="name" let-row><strong>{{ problem(row).name }}</strong></ng-template>
            <ng-template mcCell="issue" let-row>{{ i18n.t('dashboard.issues.' + problem(row).issue) }}</ng-template>
            <ng-template mcCell="locationName" let-row>{{ problem(row).locationName ?? '—' }}</ng-template>
            <ng-template mcCell="health" let-row><mc-status-pill [status]="problem(row).connection === 'Offline' ? 'offline' : problem(row).health" /></ng-template>
            <ng-template mcCell="lastSeenAt" let-row>{{ seen(problem(row).lastSeenAt) }}</ng-template>
          </mc-data-table>
        </mc-card>
      </section>

      <section class="row">
        <mc-card [title]="i18n.t('dashboard.recentAlerts')" class="span-2">
          <mc-empty-state icon="bell" [title]="i18n.t('dashboard.noAlerts')" [message]="i18n.t('dashboard.alertsSoon')" />
        </mc-card>
        <mc-card [title]="i18n.t('dashboard.locationSummary')" data-testid="location-summary">
          <dl class="summary">
            <dt>{{ i18n.t('locations.address') }}</dt><dd>{{ address() }}</dd>
            <dt>{{ i18n.t('dashboard.contact') }}</dt><dd>{{ d.location.contactName ?? '—' }}@if (d.location.contactPhone) { · {{ d.location.contactPhone }} }</dd>
            <dt>{{ i18n.t('locations.timeZone') }}</dt><dd>{{ d.location.timeZone }}</dd>
            <dt>{{ i18n.t('dashboard.lastSync') }}</dt>
            <dd>
              @if (d.location.lastSyncAt) {
                {{ d.location.lastSyncAt | date: 'medium' : undefined : locale() }} · <span class="ok">{{ i18n.t('dashboard.synced') }}</span>
              } @else {
                —
              }
            </dd>
          </dl>
        </mc-card>
      </section>
    } @else if (context.loading()) {
      <section class="tiles tiles-6">
        @for (i of [1, 2, 3, 4, 5, 6]; track i) {
          <mc-skeleton [height]="96" />
        }
      </section>
    }
  `,
  styleUrl: '../dashboard/dashboard.scss',
  styles: `
    .span-2 { grid-column: span 2; }
    .ok { color: var(--mc-success); }
    .rings + .muted { margin-block-start: var(--mc-space-3); text-align: center; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LocationOverviewPage {
  protected readonly i18n = inject(I18nService);
  protected readonly context = inject(LocationContext);
  protected readonly locale = computed(() => (this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB'));
  protected readonly tiles = computed(() => tileViews(this.i18n, this.context.dashboard()?.tiles ?? []));

  protected readonly os = computed<DonutSegment[]>(() =>
    (this.context.dashboard()?.devicesByOs ?? []).map((o) => ({ key: o.name, label: this.i18n.t(`os.${o.name.toLowerCase()}`), value: o.count, color: OS_COLORS[o.name] ?? 'mc-series-other' })),
  );

  protected readonly health = computed<DonutSegment[]>(() => {
    const h = this.context.dashboard()?.deviceHealth;
    return h
      ? [
          { key: 'healthy', label: this.i18n.t('status.healthy'), value: h.healthy, color: 'mc-success' },
          { key: 'warning', label: this.i18n.t('status.warning'), value: h.warning, color: 'mc-warning' },
          { key: 'critical', label: this.i18n.t('status.critical'), value: h.critical, color: 'mc-danger' },
          { key: 'offline', label: this.i18n.t('status.offline'), value: h.offline, color: 'mc-neutral' },
        ]
      : [];
  });

  protected readonly address = computed(() => {
    const l = this.context.dashboard()?.location;
    return l ? [l.addressLine, l.city, l.country].filter((p) => !!p).join(', ') || '—' : '—';
  });

  protected readonly problemColumns = computed<Column[]>(() => [
    { key: 'name', label: this.i18n.t('dashboard.deviceName') },
    { key: 'issue', label: this.i18n.t('dashboard.issue') },
    { key: 'locationName', label: this.i18n.t('dashboard.location') },
    { key: 'health', label: this.i18n.t('dashboard.severity') },
    { key: 'lastSeenAt', label: this.i18n.t('device.lastSeen') },
  ]);

  protected problem(row: unknown): ProblemDevice {
    return row as ProblemDevice;
  }

  protected seen(at: string | null): string {
    return relativeTime(this.i18n, at);
  }
}
