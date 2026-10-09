import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

import { DevicesApi, LocationsApi } from '../../core/api/api.services';
import { DeviceListItem, LocationCard } from '../../core/api/models';
import { Report, ReportType, ReportsApi, saveFile } from '../../core/api/reports.api';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { ToastService } from '../../core/ui/toast.service';
import { Button } from '../../shared/ui/button';
import { Card } from '../../shared/ui/card';
import { Icon } from '../../shared/ui/icon';
import { Skeleton } from '../../shared/ui/skeleton';
import { EmptyState, ErrorState } from '../../shared/ui/states';
import { StatusPill } from '../../shared/ui/status-pill';

/** Report types that make sense for one device. */
const DEVICE_TYPES = ['device-health', 'performance', 'network-usage', 'alerts', 'custom'];
/** Report types that make sense for one location. */
const LOCATION_TYPES = ['overview', 'location-summary', 'device-health', 'performance', 'network-usage', 'alerts', 'custom'];
const RANGES: Record<string, number> = { '24h': 1, '7d': 7, '30d': 30, '90d': 90 };
const POLL_MS = 3000;

/** Byte size as "12 KB" / "1.4 MB". */
export function fileSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`;
  }
  if (bytes < 1024 * 1024) {
    return `${Math.round(bytes / 1024)} KB`;
  }
  return `${Math.round((bytes / (1024 * 1024)) * 10) / 10} MB`;
}

/**
 * The Reports screen (07 section 5.8): Report Type list, Report Options and Recent Reports. With `locationId` or `deviceId`
 * the scope is fixed and the type list is filtered (location and device tabs).
 */
@Component({
  selector: 'mc-reports-panel',
  imports: [FormsModule, Card, Button, Icon, Skeleton, EmptyState, ErrorState, StatusPill],
  template: `
    @if (failed()) {
      <mc-error-state (retry)="load()" />
    } @else {
      <div class="columns">
        <mc-card [title]="i18n.t('reports.type')" [flush]="true" data-testid="report-types">
          @if (types(); as list) {
            <ul class="types" role="listbox" [attr.aria-label]="i18n.t('reports.type')">
              @for (t of list; track t.type) {
                <li>
                  <button type="button" role="option" [class.selected]="selected() === t.type" [attr.aria-selected]="selected() === t.type" [disabled]="!t.entitled"
                    (click)="selected.set(t.type)" [attr.data-testid]="'type-' + t.type">
                    <mc-icon name="reports" [size]="16" />
                    <span>{{ i18n.t('reports.types.' + t.type) }}</span>
                    @if (!t.entitled) {
                      <small>{{ i18n.t('settings.notInPlan') }}</small>
                    }
                  </button>
                </li>
              }
            </ul>
          } @else {
            <mc-skeleton [height]="300" />
          }
        </mc-card>

        <mc-card [title]="i18n.t('reports.options')" data-testid="report-options">
          <form class="mc-form" (ngSubmit)="generate()">
            @if (!locationId() && !deviceId()) {
              <fieldset class="locations">
                <legend>{{ i18n.t('nav.locations') }}</legend>
                <label class="inline"><input type="checkbox" name="allLocations" [checked]="chosenLocations().length === 0" (change)="chosenLocations.set([])" /> {{ i18n.t('settings.allLocations') }}</label>
                @for (l of locations(); track l.id) {
                  <label class="inline"><input type="checkbox" [name]="'l-' + l.id" [checked]="chosenLocations().includes(l.id)" (change)="toggleLocation(l.id)" /> {{ l.name }}</label>
                }
              </fieldset>
            }
            @if (!deviceId()) {
              <label><span>{{ i18n.t('nav.devices') }}</span>
                <select name="device" [ngModel]="device()" (ngModelChange)="device.set($event)" data-testid="report-device">
                  <option value="">{{ i18n.t('reports.allDevices') }}</option>
                  @for (d of devices(); track d.id) {
                    <option [value]="d.id">{{ deviceLabel(d) }}</option>
                  }
                </select>
              </label>
            }
            <label><span>{{ i18n.t('reports.timeRange') }}</span>
              <select name="range" [ngModel]="range()" (ngModelChange)="range.set($event)" data-testid="report-range">
                @for (r of ranges; track r) {
                  <option [value]="r">{{ i18n.t('range.' + r) }}</option>
                }
              </select>
            </label>
            @if (!deviceId()) {
              <label><span>{{ i18n.t('reports.groupBy') }}</span>
                <select name="groupBy" [ngModel]="groupBy()" (ngModelChange)="groupBy.set($event)" [disabled]="selected() !== 'performance'" data-testid="report-group">
                  <option value="device">{{ i18n.t('reports.byDevice') }}</option>
                  <option value="location">{{ i18n.t('reports.byLocation') }}</option>
                </select>
              </label>
            }
            <label><span>{{ i18n.t('reports.format') }}</span>
              <select name="format" [ngModel]="format()" (ngModelChange)="format.set($event)" data-testid="report-format">
                <option value="csv">CSV</option>
                <option value="pdf" [disabled]="!pdfAvailable()">PDF{{ pdfAvailable() ? '' : ' — ' + i18n.t('reports.pdfUnavailable') }}</option>
              </select>
            </label>
            <div class="actions">
              <button type="submit" mcButton="primary-solid" [disabled]="busy() || !canGenerate() || !selectedType()?.entitled" data-testid="generate-report">
                {{ i18n.t('reports.generate') }}
              </button>
            </div>
          </form>
        </mc-card>

        <mc-card [title]="i18n.t('reports.recent')" [flush]="true" data-testid="recent-reports">
          @if (reports(); as list) {
            @if (list.length === 0) {
              <mc-empty-state icon="reports" [title]="i18n.t('reports.none')" />
            } @else {
              <ul class="recent">
                @for (r of list; track r.id) {
                  <li [attr.data-testid]="'report-' + r.id">
                    <mc-icon name="file" [size]="18" />
                    <div class="what">
                      <strong>{{ r.title }}</strong>
                      <small>{{ date(r.requestedAt) }} · {{ r.format.toUpperCase() }}@if (r.status === 'Done') { · {{ size(r.sizeBytes) }} }</small>
                    </div>
                    @if (r.status === 'Done') {
                      <button type="button" mcButton="ghost" size="sm" (click)="download(r)" [attr.aria-label]="i18n.t('reports.download') + ' ' + r.title" data-testid="download-report">
                        <mc-icon name="download" [size]="16" />
                      </button>
                    } @else {
                      <mc-status-pill [status]="statusOf(r)" [label]="i18n.t('reports.status.' + r.status)" [attr.title]="r.error ?? ''" />
                    }
                  </li>
                }
              </ul>
            }
          } @else {
            <mc-skeleton [height]="300" />
          }
        </mc-card>
      </div>
    }
  `,
  styles: `
    :host { display: block; }
    .columns { display: grid; grid-template-columns: minmax(220px, 0.9fr) minmax(280px, 1.1fr) minmax(280px, 1.2fr); gap: var(--mc-space-4); align-items: start; }
    @media (max-width: 1200px) { .columns { grid-template-columns: 1fr 1fr; } }
    @media (max-width: 760px) { .columns { grid-template-columns: 1fr; } }
    .types, .recent { list-style: none; margin: 0; padding: 0; }
    .types button { inline-size: 100%; display: flex; align-items: center; gap: var(--mc-space-2); padding: var(--mc-space-3) var(--mc-space-4); background: transparent; border: 0;
      border-inline-start: 3px solid transparent; color: var(--mc-text); font: inherit; text-align: start; cursor: pointer; }
    .types button:hover:not(:disabled) { background: var(--mc-bg-hover); }
    .types button.selected { background: var(--mc-bg-nav-active); border-inline-start-color: var(--mc-brand); color: var(--mc-brand); font-weight: var(--mc-fw-medium); }
    .types button:disabled { color: var(--mc-text-muted); cursor: not-allowed; }
    .types small { margin-inline-start: auto; color: var(--mc-text-muted); font-size: var(--mc-fs-xs); }
    .locations { border: 0; padding: 0; margin: 0; display: flex; flex-direction: column; gap: var(--mc-space-1); max-block-size: 180px; overflow-y: auto; }
    .locations legend { color: var(--mc-text-muted); font-size: var(--mc-fs-sm); margin-block-end: var(--mc-space-1); }
    .inline { flex-direction: row; align-items: center; gap: var(--mc-space-2); }
    .actions { display: flex; justify-content: flex-end; margin-block-start: var(--mc-space-3); }
    .recent li { display: flex; align-items: center; gap: var(--mc-space-3); padding: var(--mc-space-3) var(--mc-space-4); border-block-end: 1px solid var(--mc-border); }
    .recent li:last-child { border-block-end: 0; }
    .what { flex: 1; min-inline-size: 0; display: flex; flex-direction: column; gap: 2px; }
    .what strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .what small { color: var(--mc-text-muted); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportsPanel {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(ReportsApi);
  private readonly locationsApi = inject(LocationsApi);
  private readonly devicesApi = inject(DevicesApi);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  /** Fixed location (location tab). */
  readonly locationId = input<string | null>(null);
  /** Fixed device (device tab). */
  readonly deviceId = input<string | null>(null);

  protected readonly ranges = Object.keys(RANGES);
  private readonly allTypes = signal<ReportType[] | null>(null);
  protected readonly pdfAvailable = signal(false);
  protected readonly reports = signal<Report[] | null>(null);
  protected readonly locations = signal<LocationCard[]>([]);
  protected readonly devices = signal<DeviceListItem[]>([]);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);

  protected readonly selected = signal('overview');
  protected readonly chosenLocations = signal<string[]>([]);
  protected readonly device = signal('');
  protected readonly range = signal('7d');
  protected readonly groupBy = signal('device');
  protected readonly format = signal('csv');

  protected readonly canGenerate = computed(() => this.auth.hasPermission('reports.generate'));
  protected readonly types = computed(() => {
    const all = this.allTypes();
    if (!all) {
      return null;
    }
    const allowed = this.deviceId() ? DEVICE_TYPES : this.locationId() ? LOCATION_TYPES : null;
    return allowed ? all.filter((t) => allowed.includes(t.type)) : all;
  });
  protected readonly selectedType = computed(() => this.types()?.find((t) => t.type === this.selected()) ?? null);

  private timer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopPolling());
    effect(() => {
      const types = this.types();
      if (types && !types.some((t) => t.type === this.selected())) {
        untracked(() => this.selected.set(types.find((t) => t.entitled)?.type ?? types[0]?.type ?? ''));
      }
    });
    effect(() => {
      const location = this.locationId();
      const chosen = this.chosenLocations();
      untracked(() => void this.loadDevices(location ? [location] : chosen));
    });
    void this.load();
  }

  async load(): Promise<void> {
    this.failed.set(false);
    try {
      const types = await firstValueFrom(this.api.types());
      this.pdfAvailable.set(types.pdfAvailable);
      this.allTypes.set(types.types);
      if (!this.locationId() && !this.deviceId()) {
        this.locations.set((await firstValueFrom(this.locationsApi.list({ pageSize: 200 }))).items);
      }
      await this.refresh();
    } catch {
      this.failed.set(true);
    }
  }

  private async loadDevices(locations: string[]): Promise<void> {
    if (this.deviceId()) {
      return;
    }
    try {
      const result = await firstValueFrom(this.devicesApi.list({ pageSize: 200, sort: 'name', locationId: locations.length === 1 ? locations[0] : null }));
      this.devices.set(locations.length > 1 ? result.items.filter((d) => locations.includes(d.locationId)) : result.items);
      if (this.device() && !this.devices().some((d) => d.id === this.device())) {
        this.device.set('');
      }
    } catch {
      this.devices.set([]);
    }
  }

  /** Names repeat across locations, so the option also shows the location (or the IP inside one location). */
  protected deviceLabel(device: DeviceListItem): string {
    const location = this.locations().find((l) => l.id === device.locationId)?.name;
    const detail = location ?? device.localIp;
    return detail ? `${device.name} · ${detail}` : device.name;
  }

  protected toggleLocation(id: string): void {
    this.chosenLocations.update((list) => (list.includes(id) ? list.filter((l) => l !== id) : [...list, id]));
  }

  protected async generate(): Promise<void> {
    const type = this.selectedType();
    if (!type?.entitled) {
      return;
    }
    this.busy.set(true);
    const to = new Date();
    const from = new Date(to.getTime() - RANGES[this.range()] * 86_400_000);
    const location = this.locationId();
    const device = this.deviceId() ?? (this.device() || null);
    try {
      await firstValueFrom(
        this.api.request({
          type: type.type,
          locationIds: location ? [location] : this.chosenLocations(),
          deviceIds: device ? [device] : [],
          from: from.toISOString(),
          to: to.toISOString(),
          groupBy: type.type === 'performance' && !this.deviceId() ? this.groupBy() : 'device',
          format: this.format(),
        }),
      );
      this.toast.success(this.i18n.t('reports.queued'));
      await this.refresh();
    } catch {
      // The error toast explains why.
    } finally {
      this.busy.set(false);
    }
  }

  protected async download(report: Report): Promise<void> {
    try {
      saveFile(await firstValueFrom(this.api.download(report.id)));
    } catch {
      // The error toast explains why.
    }
  }

  private async refresh(): Promise<void> {
    this.stopPolling();
    const list = (await firstValueFrom(this.api.list(1, 10))).items;
    this.reports.set(list);
    if (list.some((r) => r.status === 'Queued' || r.status === 'Running')) {
      this.timer = setTimeout(() => void this.refresh().catch(() => undefined), POLL_MS);
    }
  }

  private stopPolling(): void {
    if (this.timer) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }

  protected date(at: string): string {
    return new Date(at).toLocaleString(this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB', { dateStyle: 'medium', timeStyle: 'short' });
  }

  protected size(bytes: number): string {
    return fileSize(bytes);
  }

  protected statusOf(report: Report): string {
    return report.status === 'Failed' ? 'critical' : 'info';
  }
}
