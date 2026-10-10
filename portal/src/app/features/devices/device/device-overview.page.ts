import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { debounceTime, filter, firstValueFrom } from 'rxjs';

import { DevicesApi } from '../../../core/api/api.services';
import { DeviceDisk, DeviceMetrics, InventoryDoc } from '../../../core/api/models';
import { I18nService } from '../../../core/i18n/i18n.service';
import { DateRangeStore } from '../../../core/state/date-range.store';
import { uptime } from '../../../shared/format';
import { AccordionItem } from '../../../shared/ui/accordion';
import { Card } from '../../../shared/ui/card';
import { CellDef, Column, DataTable } from '../../../shared/ui/data-table';
import { ProgressBar } from '../../../shared/ui/progress-bar';
import { RingGauge } from '../../../shared/ui/ring-gauge';
import { SpeedGauge } from '../../../shared/ui/speed-gauge';
import { EmptyState } from '../../../shared/ui/states';
import { StatusPill } from '../../../shared/ui/status-pill';
import { TrendChart, TrendSeries } from '../../../shared/ui/trend-chart';
import { DeviceContext } from './device-context';
import { MessagesBoard, MonitorPoints } from './device-monitoring';
import { Alert, AlertsApi, MonitorPoint } from '../../../core/api/monitoring.api';
import { LiveService } from '../../../core/live/live.service';

const RANGE_HOURS: Record<string, number> = { '24h': 24, '7d': 168, '30d': 720, '90d': 2160 };

interface MetricCard {
  key: 'cpu' | 'ram' | 'disk' | 'network';
  value: number | null;
  tone: 'cpu' | 'ram' | 'disk' | 'info';
  facts: { label: string; value: string }[];
  series: TrendSeries[];
  top: { name: string; pid: number; value: number }[];
}

/** Device overview (07 section 5.7). */
@Component({
  selector: 'mc-device-overview-page',
  imports: [Card, RingGauge, SpeedGauge, TrendChart, ProgressBar, DataTable, CellDef, StatusPill, EmptyState, AccordionItem, MonitorPoints, MessagesBoard],
  templateUrl: './device-overview.page.html',
  styleUrl: './device-overview.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DeviceOverviewPage {
  protected readonly i18n = inject(I18nService);
  protected readonly context = inject(DeviceContext);
  private readonly api = inject(DevicesApi);
  private readonly range = inject(DateRangeStore);

  protected readonly metrics = signal<DeviceMetrics | null>(null);
  protected readonly inventory = signal<Record<string, InventoryDoc | null>>({});
  protected readonly alerts = signal<Alert[]>([]);
  protected readonly points = signal<MonitorPoint[]>([]);
  private readonly alertsApi = inject(AlertsApi);

  protected readonly diskColumns = computed<Column[]>(() => [
    { key: 'drive', label: this.i18n.t('device.drive') },
    { key: 'label', label: this.i18n.t('device.label') },
    { key: 'fileSystem', label: this.i18n.t('device.fileSystem') },
    { key: 'totalGb', label: this.i18n.t('device.total') },
    { key: 'usedGb', label: this.i18n.t('device.used') },
    { key: 'freeGb', label: this.i18n.t('device.free') },
    { key: 'usagePercent', label: this.i18n.t('device.usage') },
    { key: 'status', label: this.i18n.t('common.status') },
  ]);

  protected readonly cards = computed<MetricCard[]>(() => {
    const s = this.context.snapshot();
    const live = this.context.live();
    const m = this.metrics();
    const series = (metric: string, color: string, name: string, useMax = false): TrendSeries[] => {
      const points = m?.series.find((x) => x.metric === metric)?.points ?? [];
      return [{ name, color, points: points.map((p) => ({ at: p.at, value: (useMax ? p.max : p.avg) ?? null })) }];
    };
    const n = (v: number | undefined | null, unit = '') => (v === undefined || v === null ? '—' : `${Math.round(v * 10) / 10}${unit}`);
    const kb = (v: number | undefined | null) => (v === undefined || v === null ? '—' : `${Math.round(v / 1024)} KB/s`);
    return [
      {
        key: 'cpu', tone: 'cpu', value: live?.cpu ?? s?.cpu?.usage ?? null,
        facts: [
          { label: this.i18n.t('device.fact.usage'), value: n(live?.cpu ?? s?.cpu?.usage, '%') },
          { label: this.i18n.t('device.fact.cores'), value: s?.cpu ? `${s.cpu.physicalCores ?? '—'} / ${s.cpu.logicalCores ?? '—'}` : '—' },
          { label: this.i18n.t('device.fact.speed'), value: n(s?.cpu?.speedGhz, ' GHz') },
          { label: this.i18n.t('device.fact.temp'), value: n(live?.cpuTempC ?? s?.cpu?.tempC, ' °C') },
          { label: this.i18n.t('device.fact.processes'), value: n(s?.cpu?.processes) },
          { label: this.i18n.t('device.fact.model'), value: s?.cpu?.model ?? '—' },
        ],
        series: series('cpu', 'mc-series-cpu', this.i18n.t('device.cpu')),
        top: s?.top?.cpu ?? [],
      },
      {
        key: 'ram', tone: 'ram', value: live?.ram ?? s?.ram?.usage ?? null,
        facts: [
          { label: this.i18n.t('device.fact.usage'), value: n(live?.ram ?? s?.ram?.usage, '%') },
          { label: this.i18n.t('device.fact.total'), value: n(s?.ram?.totalGb, ' GB') },
          { label: this.i18n.t('device.fact.used'), value: n(s?.ram?.usedGb, ' GB') },
          { label: this.i18n.t('device.fact.free'), value: n(s?.ram?.freeGb, ' GB') },
          { label: this.i18n.t('device.fact.cached'), value: n(s?.ram?.cachedGb, ' GB') },
        ],
        series: series('ram', 'mc-series-ram', this.i18n.t('device.ram')),
        top: s?.top?.ram ?? [],
      },
      {
        key: 'disk', tone: 'disk', value: live?.diskActive ?? s?.diskActivity?.activePercent ?? null,
        facts: [
          { label: this.i18n.t('device.fact.active'), value: n(live?.diskActive ?? s?.diskActivity?.activePercent, '%') },
          { label: this.i18n.t('device.fact.read'), value: kb(s?.diskActivity?.readBps) },
          { label: this.i18n.t('device.fact.write'), value: kb(s?.diskActivity?.writeBps) },
          { label: this.i18n.t('device.fact.response'), value: n(s?.diskActivity?.responseMs, ' ms') },
          { label: this.i18n.t('device.fact.used'), value: n(s?.partitions?.reduce((a, p) => a + (p.usedGb ?? 0), 0), ' GB') },
          { label: this.i18n.t('device.fact.free'), value: n(s?.partitions?.reduce((a, p) => a + (p.freeGb ?? 0), 0), ' GB') },
        ],
        series: series('disk', 'mc-series-disk', this.i18n.t('device.disk')),
        top: s?.top?.disk ?? [],
      },
      {
        key: 'network', tone: 'info', value: null,
        facts: [
          { label: this.i18n.t('device.fact.adapter'), value: s?.network?.adapter ?? '—' },
          { label: this.i18n.t('device.fact.download'), value: kb(live?.rxBps ?? s?.network?.downloadBps) },
          { label: this.i18n.t('device.fact.upload'), value: kb(live?.txBps ?? s?.network?.uploadBps) },
          { label: this.i18n.t('device.fact.speedTest'), value: s?.network?.lastSpeedTest ? `${n(s.network.lastSpeedTest.downloadMbps)} / ${n(s.network.lastSpeedTest.uploadMbps)} Mbps` : '—' },
          { label: this.i18n.t('device.fact.publicIp'), value: s?.network?.publicIp ?? this.context.device()?.publicIp ?? '—' },
          { label: this.i18n.t('device.fact.localIp'), value: s?.network?.localIp ?? this.context.device()?.localIp ?? '—' },
          { label: this.i18n.t('device.fact.ping'), value: n(s?.network?.pingMs, ' ms') },
          { label: this.i18n.t('device.fact.loss'), value: n(s?.network?.lossPercent, '%') },
        ],
        series: [
          ...series('network', 'mc-series-network', this.i18n.t('device.fact.download')),
          ...series('network', 'mc-series-ram', this.i18n.t('device.fact.upload'), true),
        ],
        top: s?.top?.network ?? [],
      },
    ];
  });

  /** Mbps for the speed gauge from the live or snapshot download rate (bytes per second). */
  protected readonly downloadMbps = computed(() => {
    const bps = this.context.live()?.rxBps ?? this.context.snapshot()?.network?.downloadBps;
    return bps === undefined || bps === null ? null : Math.round((bps * 8) / 100_000) / 10;
  });

  protected readonly serviceUptime = computed(() => uptime(this.i18n, this.context.snapshot()?.service?.uptimeSeconds));

  constructor() {
    effect(() => {
      const id = this.context.id();
      const hours = RANGE_HOURS[this.range.range()] ?? 168;
      if (id) {
        void this.loadMetrics(id, hours);
      }
    });
    effect(() => {
      const id = this.context.id();
      if (id) {
        void this.loadInventory(id);
        void this.loadMonitoring(id);
      }
    });
    inject(LiveService).alert$.pipe(filter((e) => e.deviceId === this.context.id()), debounceTime(500), takeUntilDestroyed()).subscribe(() => void this.loadMonitoring(this.context.id()));
  }

  private async loadMetrics(id: string, hours: number): Promise<void> {
    const to = new Date();
    const from = new Date(to.getTime() - hours * 3_600_000);
    try {
      this.metrics.set(await firstValueFrom(this.api.metrics(id, from.toISOString(), to.toISOString(), 'cpu,ram,disk,network')));
    } catch {
      this.metrics.set(null);
    }
  }

  private async loadMonitoring(id: string): Promise<void> {
    const [alerts, points] = await Promise.all([
      firstValueFrom(this.alertsApi.device(id, 'all', 10)).then((p) => p.items).catch(() => [] as Alert[]),
      firstValueFrom(this.alertsApi.monitorPoints(id)).catch(() => [] as MonitorPoint[]),
    ]);
    this.alerts.set(alerts);
    this.points.set(points);
  }

  private async loadInventory(id: string): Promise<void> {
    const result: Record<string, InventoryDoc | null> = {};
    await Promise.all(
      ['hardware', 'os', 'network'].map(async (kind) => {
        try {
          result[kind] = await firstValueFrom(this.api.inventory(id, kind));
        } catch {
          result[kind] = null;
        }
      }),
    );
    this.inventory.set(result);
  }

  protected readonly diskKey = (row: DeviceDisk) => row.drive;

  protected disk(row: unknown): DeviceDisk {
    return row as DeviceDisk;
  }

  /** Key/value pairs of an inventory document's top level (nested objects flattened one level). */
  protected entries(doc: InventoryDoc | null | undefined): { key: string; value: string }[] {
    const document = doc?.document as Record<string, unknown> | undefined;
    if (!document || typeof document !== 'object') {
      return [];
    }
    return Object.entries(document).flatMap(([key, value]) =>
      value !== null && typeof value === 'object' && !Array.isArray(value)
        ? Object.entries(value as Record<string, unknown>).map(([k, v]) => ({ key: `${key}.${k}`, value: String(v) }))
        : [{ key, value: Array.isArray(value) ? `${value.length}` : String(value) }],
    );
  }

  /** `cpu.logicalProcessors` as "CPU · Logical processors". */
  protected label(key: string): string {
    const words = (part: string) => {
      const text = part.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/Gb$/, 'GB').toLowerCase();
      return /^(cpu|ram|os|ip|dns|bios|mac)$/.test(text) ? text.toUpperCase() : text.charAt(0).toUpperCase() + text.slice(1);
    };
    return key.split('.').map(words).join(' · ');
  }

  /** One line per Hardware & OS section, as in the design: the main facts when present, else the first values. */
  protected summary(kind: string): string {
    const entries = this.entries(this.inventory()[kind]);
    if (entries.length === 0) {
      return '';
    }
    const value = (...keys: string[]) => keys.map((k) => entries.find((e) => e.key === k)?.value).find((v) => !!v && v !== 'undefined');
    const parts =
      kind === 'hardware'
        ? [
            value('cpu.model'),
            value('cpu.cores') && `${value('cpu.cores')} ${this.i18n.t('device.fact.cores').toLowerCase()}${value('cpu.logicalProcessors') ? ` (${value('cpu.logicalProcessors')})` : ''}`,
            value('memory.totalGb') && `${value('memory.totalGb')} GB RAM`,
          ]
        : kind === 'os'
          ? [value('name', 'caption', 'os.name', 'productName'), value('version', 'os.version', 'displayVersion'), value('build', 'os.build'), value('architecture', 'os.architecture')]
          : [value('ipAddress', 'ip', 'localIp', 'adapters.0.ip'), value('dns', 'dnsServers'), value('gateway', 'defaultGateway')];
    const known = parts.filter((p): p is string => !!p);
    return (known.length ? known : entries.slice(0, 3).map((e) => e.value)).join(' | ');
  }

  /** Top 5 values with the unit of the card (snapshot: CPU %, RAM MB, disk and network KB/s). */
  protected topValue(card: string, value: number): string {
    const rounded = Math.round(value * 10) / 10;
    return card === 'cpu' ? `${rounded}%` : card === 'ram' ? `${Math.round(value)} MB` : `${rounded} KB/s`;
  }

  /** Bar length relative to the largest value of the list. */
  protected topShare(top: { value: number }[], value: number): number {
    const max = Math.max(...top.slice(0, 5).map((p) => p.value), 0);
    return max > 0 ? Math.round((100 * value) / max) : 0;
  }
}
