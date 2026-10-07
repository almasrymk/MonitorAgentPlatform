import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import { DeviceListItem } from '../../core/api/models';
import { I18nService } from '../../core/i18n/i18n.service';
import { relativeTime, uptime } from '../format';
import { Button } from './button';
import { CountBadge } from './count-badge';
import { Menu, MenuItem } from './menu';
import { OsIcon } from './os-icon';
import { ProgressBar } from './progress-bar';
import { StatusPill } from './status-pill';

export type DeviceAction = 'rename' | 'move' | 'unlicense' | 'retire';

/** A device on the Location devices / Devices screens (07 section 5.6). */
@Component({
  selector: 'mc-device-card',
  imports: [OsIcon, StatusPill, CountBadge, Menu, ProgressBar, Button],
  template: `
    <header>
      <span class="os"><mc-os-icon [os]="device().osFamily" [size]="22" /></span>
      <div class="title">
        <h3 data-testid="device-name">{{ device().name }}</h3>
        <span class="muted">{{ device().osName ?? device().osFamily }}</span>
        <span class="muted mono">{{ device().localIp ?? '—' }}</span>
      </div>
      @if (device().openAlerts > 0) {
        <mc-count-badge [count]="device().openAlerts" [severity]="device().openAlertSeverity" [label]="i18n.t('device.openAlerts', { n: device().openAlerts })" data-testid="alert-badge" />
      }
      @if (menuItems().length) {
        <mc-menu [items]="menuItems()" [label]="i18n.t('device.actions')" (selected)="onMenu($event)" />
      }
    </header>
    <div class="pills">
      <mc-status-pill [status]="statusKey()" data-testid="device-status" />
      <mc-status-pill [status]="device().licenseState" data-testid="device-license" />
    </div>
    <div class="bars">
      <mc-progress-bar [label]="i18n.t('device.cpu')" tone="cpu" [value]="device().cpu" />
      <mc-progress-bar [label]="i18n.t('device.ram')" tone="ram" [value]="device().ram" />
      <mc-progress-bar [label]="i18n.t('device.disk')" tone="disk" [value]="device().disk" />
    </div>
    <dl>
      <div><dt>{{ i18n.t('device.lastSeen') }}</dt><dd data-testid="last-seen">{{ lastSeen() }}</dd></div>
      <div><dt>{{ i18n.t('device.uptime') }}</dt><dd>{{ up() }}</dd></div>
    </dl>
    <footer>
      <button type="button" mcButton="primary-outline" size="sm" data-testid="open-console" [disabled]="device().connection !== 'Online'" (click)="open.emit({ device: device(), live: true })">{{ i18n.t('device.openConsole') }}</button>
      <button type="button" mcButton="secondary" size="sm" data-testid="device-details" (click)="open.emit({ device: device(), live: false })">{{ i18n.t('device.details') }}</button>
    </footer>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: var(--mc-space-3); background: var(--mc-bg-card); border-radius: var(--mc-radius-lg); box-shadow: var(--mc-shadow-card); padding: var(--mc-space-4); min-inline-size: 0; }
    header { display: flex; align-items: flex-start; gap: var(--mc-space-3); }
    .os { display: inline-flex; padding: var(--mc-space-2); border-radius: var(--mc-radius-md); background: var(--mc-bg-card-raised); }
    .title { flex: 1; min-inline-size: 0; display: flex; flex-direction: column; }
    h3 { margin: 0; font-size: var(--mc-fs-md); font-weight: var(--mc-fw-semibold); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .muted { color: var(--mc-text-muted); font-size: var(--mc-fs-xs); }
    .mono { font-family: var(--mc-font-mono); }
    .pills { display: flex; gap: var(--mc-space-2); flex-wrap: wrap; }
    .bars { display: flex; flex-direction: column; gap: var(--mc-space-2); }
    dl { display: grid; grid-template-columns: 1fr 1fr; gap: var(--mc-space-2); margin: 0; font-size: var(--mc-fs-xs); }
    dt { color: var(--mc-text-faint); }
    dd { margin: 0; color: var(--mc-text-secondary); }
    footer { display: flex; gap: var(--mc-space-2); }
    footer button { flex: 1; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DeviceCard {
  protected readonly i18n = inject(I18nService);
  readonly device = input.required<DeviceListItem>();
  readonly canManage = input(false);
  readonly action = output<{ action: DeviceAction; device: DeviceListItem }>();
  readonly open = output<{ device: DeviceListItem; live: boolean }>();

  /** Offline devices show "Offline"; online ones their health. */
  readonly statusKey = computed(() => (this.device().connection === 'Online' ? (this.device().health === 'Unknown' ? 'online' : this.device().health) : 'offline'));
  readonly lastSeen = computed(() => relativeTime(this.i18n, this.device().lastSeenAt));
  readonly up = computed(() => uptime(this.i18n, this.device().uptimeSeconds));
  protected onMenu(id: string): void {
    this.action.emit({ action: id as DeviceAction, device: this.device() });
  }

  readonly menuItems = computed<MenuItem[]>(() =>
    this.canManage()
      ? [
          { id: 'rename', label: this.i18n.t('device.rename') },
          { id: 'move', label: this.i18n.t('device.move') },
          { id: 'unlicense', label: this.i18n.t('device.unlicense'), disabled: this.device().licenseState !== 'Licensed' },
          { id: 'retire', label: this.i18n.t('device.retire'), danger: true },
        ]
      : [],
  );
}
