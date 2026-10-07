import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { DevicesApi, LocationsApi } from '../../core/api/api.services';
import { DeviceListItem, DevicesSummary, LocationCard, Paged } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { areaRoot } from '../../core/layout/area';
import { ToastService } from '../../core/ui/toast.service';
import { relativeTime } from '../../shared/format';
import { Button } from '../../shared/ui/button';
import { Card } from '../../shared/ui/card';
import { CellDef, Column, DataTable } from '../../shared/ui/data-table';
import { DeviceAction, DeviceCard } from '../../shared/ui/device-card';
import { Dialog } from '../../shared/ui/dialog';
import { Icon } from '../../shared/ui/icon';
import { KpiTile } from '../../shared/ui/kpi-tile';
import { OsIcon } from '../../shared/ui/os-icon';
import { Pagination } from '../../shared/ui/pagination';
import { SearchInput } from '../../shared/ui/search-input';
import { Select, SelectOption } from '../../shared/ui/select';
import { Skeleton } from '../../shared/ui/skeleton';
import { EmptyState, ErrorState } from '../../shared/ui/states';
import { StatusPill } from '../../shared/ui/status-pill';
import { ViewMode, ViewToggle } from '../../shared/ui/view-toggle';
import { AddDeviceDialog } from './add-device-dialog';

interface PendingAction {
  action: DeviceAction;
  device: DeviceListItem;
  name: string;
  locationId: string;
}

/**
 * The device grid / list with tiles, filters, actions and the enrollment dialog. Used by Location devices, Devices
 * (all) and the device tabs of Subscription & Licenses.
 */
@Component({
  selector: 'mc-devices-browser',
  imports: [
    FormsModule, KpiTile, SearchInput, Select, ViewToggle, Button, Icon, DeviceCard, DataTable, CellDef, OsIcon, StatusPill, Pagination, Skeleton, EmptyState, ErrorState,
    Dialog, Card, AddDeviceDialog,
  ],
  templateUrl: './devices-browser.html',
  styleUrl: './devices-browser.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DevicesBrowser {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(DevicesApi);
  private readonly locationsApi = inject(LocationsApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  readonly locationId = input<string | null>(null);
  /** Fixed licence filter (Subscription device tabs). */
  readonly license = input<string | null>(null);
  readonly showSummary = input(false);
  readonly showLocationFilter = input(false);
  readonly allowEnroll = input(false);
  readonly changed = output();

  protected readonly pageSize = 24;
  protected readonly search = signal('');
  protected readonly os = signal('');
  protected readonly status = signal('');
  protected readonly licenseFilter = signal('');
  protected readonly locationFilter = signal('');
  protected readonly sort = signal('severity');
  protected readonly page = signal(1);
  protected readonly view = signal<ViewMode>('grid');

  protected readonly result = signal<Paged<DeviceListItem> | null>(null);
  protected readonly summary = signal<DevicesSummary | null>(null);
  protected readonly locations = signal<LocationCard[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly pending = signal<PendingAction | null>(null);
  protected readonly busy = signal(false);
  protected readonly enrollOpen = signal(false);

  protected readonly canManage = computed(() => this.auth.hasPermission('devices.manage'));
  protected readonly canEnroll = computed(() => this.allowEnroll() && !!this.locationId() && this.auth.hasPermission('devices.enroll'));

  protected readonly osOptions = computed<SelectOption[]>(() => [
    { value: '', label: this.i18n.t('devices.allOs') },
    ...['windows', 'linux', 'macos', 'other'].map((os) => ({ value: os, label: this.i18n.t(`os.${os}`) })),
  ]);
  protected readonly statusOptions = computed<SelectOption[]>(() => [
    { value: '', label: this.i18n.t('devices.allStatuses') },
    ...['online', 'offline', 'healthy', 'warning', 'critical'].map((s) => ({ value: s, label: this.i18n.t(`status.${s}`) })),
  ]);
  protected readonly licenseOptions = computed<SelectOption[]>(() => [
    { value: '', label: this.i18n.t('devices.allLicenses') },
    { value: 'licensed', label: this.i18n.t('status.licensed') },
    { value: 'unlicensed', label: this.i18n.t('status.unlicensed') },
  ]);
  protected readonly locationOptions = computed<SelectOption[]>(() => [
    { value: '', label: this.i18n.t('devices.allLocations') },
    ...this.locations().map((l) => ({ value: l.id, label: l.isDefault ? this.i18n.t('locations.unassigned') : l.name })),
  ]);
  protected readonly sortOptions = computed<SelectOption[]>(() => [
    { value: 'severity', label: this.i18n.t('devices.sortSeverity') },
    { value: 'name', label: this.i18n.t('devices.sortName') },
    { value: 'lastSeen', label: this.i18n.t('devices.sortLastSeen') },
    { value: 'cpu', label: this.i18n.t('devices.sortCpu') },
  ]);
  protected readonly moveOptions = computed<SelectOption[]>(() =>
    this.locations().map((l) => ({ value: l.id, label: l.isDefault ? this.i18n.t('locations.unassigned') : l.name })),
  );

  protected readonly columns = computed<Column[]>(() => [
    { key: 'name', label: this.i18n.t('dashboard.deviceName') },
    { key: 'osFamily', label: this.i18n.t('devices.os') },
    { key: 'localIp', label: this.i18n.t('devices.ip') },
    { key: 'status', label: this.i18n.t('common.status') },
    { key: 'licenseState', label: this.i18n.t('devices.license') },
    { key: 'cpu', label: this.i18n.t('device.cpu') },
    { key: 'ram', label: this.i18n.t('device.ram') },
    { key: 'disk', label: this.i18n.t('device.disk') },
    { key: 'lastSeenAt', label: this.i18n.t('device.lastSeen') },
    ...(this.showLocationFilter() ? [{ key: 'locationName', label: this.i18n.t('dashboard.location') }] : []),
  ]);

  protected readonly tiles = computed(() => {
    const s = this.summary();
    if (!s) {
      return [];
    }
    return [
      { key: 'devices', icon: 'devices', tone: 'brand' as const, value: s.total },
      { key: 'online', icon: 'check', tone: 'success' as const, value: s.online },
      { key: 'offline', icon: 'devices', tone: 'neutral' as const, value: s.offline },
      { key: 'licensed', icon: 'subscription', tone: 'info' as const, value: s.licensed },
      { key: 'warning', icon: 'alert', tone: 'warning' as const, value: s.warning },
      { key: 'critical', icon: 'alert', tone: 'danger' as const, value: s.critical },
    ].map((t) => ({ ...t, label: this.i18n.t(`dashboard.tile.${t.key}`), caption: s.total ? this.i18n.t('dashboard.ofTotal', { percent: Math.round((t.value / s.total) * 1000) / 10 }) : undefined }));
  });

  constructor() {
    effect(() => {
      // Reload when the location or the fixed licence filter changes.
      this.locationId();
      this.license();
      untracked(() => {
        this.page.set(1);
        void this.load();
      });
    });
    void this.loadLocations();
  }

  /** Only the newest request may update the view: a slow older answer must not overwrite a newer filter. */
  private request = 0;

  async load(): Promise<void> {
    const request = ++this.request;
    this.loading.set(true);
    this.failed.set(false);
    const locationId = this.locationId() ?? (this.locationFilter() || null);
    try {
      const [result, summary] = await Promise.all([
        firstValueFrom(
          this.api.list({
            locationId, search: this.search(), os: this.os(), status: this.status(), license: this.license() ?? this.licenseFilter(), sort: this.sort(), page: this.page(), pageSize: this.pageSize,
          }),
        ),
        this.showSummary() ? firstValueFrom(this.api.summary(locationId)) : Promise.resolve(null),
      ]);
      if (request !== this.request) {
        return;
      }
      this.result.set(result);
      this.summary.set(summary);
    } catch {
      if (request === this.request) {
        this.failed.set(true);
      }
    } finally {
      if (request === this.request) {
        this.loading.set(false);
      }
    }
  }

  private async loadLocations(): Promise<void> {
    if (!this.auth.hasPermission('locations.read')) {
      return;
    }
    try {
      this.locations.set((await firstValueFrom(this.locationsApi.list({ pageSize: 200 }))).items);
    } catch {
      this.locations.set([]);
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

  protected device(row: unknown): DeviceListItem {
    return row as DeviceListItem;
  }

  protected seen(at: string | null): string {
    return relativeTime(this.i18n, at);
  }

  protected statusOf(d: DeviceListItem): string {
    return d.connection === 'Online' ? (d.health === 'Unknown' ? 'online' : d.health) : 'offline';
  }

  protected open(event: { device: DeviceListItem; live: boolean }): void {
    void this.router.navigate([areaRoot(this.router.url), 'devices', event.device.id], event.live ? { queryParams: { live: 1 } } : undefined);
  }

  protected act(event: { action: DeviceAction; device: DeviceListItem }): void {
    this.pending.set({ action: event.action, device: event.device, name: event.device.name, locationId: event.device.locationId });
  }

  protected dialogTitle(): string {
    const p = this.pending();
    return p ? this.i18n.t(`devices.dialog.${p.action}`, { name: p.device.name }) : '';
  }

  protected closeDialog(open: boolean): void {
    if (!open) {
      this.pending.set(null);
    }
  }

  async confirm(): Promise<void> {
    const p = this.pending();
    if (!p) {
      return;
    }
    this.busy.set(true);
    try {
      switch (p.action) {
        case 'rename':
        case 'move':
          await firstValueFrom(this.api.update(p.device.id, { name: p.name.trim(), locationId: p.locationId }));
          break;
        case 'unlicense':
          await firstValueFrom(this.api.unlicense(p.device.id));
          break;
        case 'retire':
          await firstValueFrom(this.api.retire(p.device.id));
          break;
      }
      this.toast.success(this.i18n.t(`devices.done.${p.action}`, { name: p.device.name }));
      this.pending.set(null);
      await this.load();
      this.changed.emit();
    } catch {
      // The error interceptor shows the problem.
    } finally {
      this.busy.set(false);
    }
  }

  protected updateName(name: string): void {
    this.pending.update((p) => (p ? { ...p, name } : p));
  }

  protected updateLocation(locationId: string): void {
    this.pending.update((p) => (p ? { ...p, locationId } : p));
  }

  protected enrolled(): void {
    void this.load();
    this.changed.emit();
  }
}
