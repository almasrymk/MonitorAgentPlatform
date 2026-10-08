import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

import { AlertsApi, ConfigurationApi, MonitorPoint, MonitorPointInput } from '../../../core/api/monitoring.api';
import { toProblem } from '../../../core/api/problem';
import { AuthService } from '../../../core/auth/auth.service';
import { I18nService } from '../../../core/i18n/i18n.service';
import { relativeTime } from '../../../shared/format';
import { Button } from '../../../shared/ui/button';
import { Card } from '../../../shared/ui/card';
import { CellDef, Column, DataTable } from '../../../shared/ui/data-table';
import { Drawer } from '../../../shared/ui/drawer';
import { Icon } from '../../../shared/ui/icon';
import { ErrorState } from '../../../shared/ui/states';
import { StatusPill } from '../../../shared/ui/status-pill';
import { DeviceContext } from './device-context';

const TYPES = ['Website', 'Database', 'Ping', 'Application', 'Service', 'Disk', 'Network', 'Custom'];
const ENGINES = ['SqlServer', 'MySql', 'PostgreSql'];

/** What the drawer edits; the type decides which fields it shows (07 section 5.7). */
interface PointForm {
  id: string | null;
  version: string;
  key: string;
  displayName: string;
  type: string;
  target: string;
  intervalSeconds: number;
  alertLevel: string;
  enabled: boolean;
  showInShortcut: boolean;
  expectStatus: number | null;
  engine: string;
  port: number | null;
}

/** Monitor Points tab (07 section 5.7): table, add / edit drawer by type, delete. Edits change the device configuration (M8). */
@Component({
  selector: 'mc-device-monitor-points-page',
  imports: [FormsModule, DatePipe, Card, DataTable, CellDef, StatusPill, Drawer, Button, Icon, ErrorState],
  template: `
    <mc-card [title]="i18n.t('device.tab.monitorPoints')" [flush]="true">
      @if (canEdit()) {
        <button cardActions type="button" mcButton="primary-outline" size="sm" (click)="open(null)" data-testid="add-point"><mc-icon name="plus" [size]="14" /> {{ i18n.t('points.add') }}</button>
      }
      @if (failed()) {
        <mc-error-state (retry)="load()" />
      } @else {
        <mc-data-table [columns]="columns()" [rows]="points()" [loading]="loading()" [emptyText]="i18n.t('device.noMonitorPoints')" data-testid="points-table">
          <ng-template mcCell="displayName" let-row><strong>{{ p(row).displayName }}</strong>@if (p(row).origin === 'Agent') { <small class="muted"> · {{ i18n.t('points.fromAgent') }}</small> }</ng-template>
          <ng-template mcCell="target" let-row><span class="target">{{ p(row).target }}</span></ng-template>
          <ng-template mcCell="status" let-row><mc-status-pill [status]="p(row).status" /></ng-template>
          <ng-template mcCell="responseMs" let-row>{{ p(row).responseMs !== null && p(row).responseMs !== undefined ? p(row).responseMs + ' ms' : '—' }}</ng-template>
          <ng-template mcCell="lastCheckedAt" let-row><span [attr.title]="p(row).lastCheckedAt | date: 'medium' : undefined : locale()">{{ seen(p(row).lastCheckedAt) }}</span></ng-template>
          <ng-template mcCell="intervalSeconds" let-row>{{ p(row).intervalSeconds }} s</ng-template>
          <ng-template mcCell="enabled" let-row><mc-status-pill [status]="p(row).enabled ? 'active' : 'inactive'" /></ng-template>
          <ng-template mcCell="actions" let-row>
            @if (canEdit()) {
              <button type="button" mcButton="ghost" size="sm" (click)="open(p(row))" [attr.data-testid]="'edit-' + p(row).key">{{ i18n.t('common.edit') }}</button>
              <button type="button" mcButton="ghost" size="sm" (click)="remove(p(row))" [attr.data-testid]="'delete-' + p(row).key">{{ i18n.t('common.delete') }}</button>
            }
          </ng-template>
        </mc-data-table>
      }
    </mc-card>

    <mc-drawer [open]="form() !== null" (openChange)="!$event && form.set(null)" [title]="form()?.id ? i18n.t('points.edit') : i18n.t('points.add')">
      @if (form(); as f) {
        <form class="mc-form" id="point-form" (ngSubmit)="save()">
          <label><span>{{ i18n.t('device.point.type') }}</span>
            <select name="type" [(ngModel)]="f.type" data-testid="point-type">
              @for (t of types; track t) {
                <option [value]="t">{{ t }}</option>
              }
            </select>
          </label>
          <label><span>{{ i18n.t('users.name') }}</span><input name="displayName" [(ngModel)]="f.displayName" required data-testid="point-name" /></label>
          <label><span>{{ i18n.t('points.target.' + f.type) }}</span><input name="target" [(ngModel)]="f.target" required data-testid="point-target" [attr.placeholder]="placeholder(f.type)" /></label>
          @if (!f.id) {
            <label><span>{{ i18n.t('points.key') }}</span><input name="key" [(ngModel)]="f.key" [attr.placeholder]="i18n.t('points.keyHint')" /></label>
          }
          @if (f.type === 'Website') {
            <label><span>{{ i18n.t('points.expectStatus') }}</span><input type="number" name="expectStatus" [(ngModel)]="f.expectStatus" min="100" max="599" /></label>
          }
          @if (f.type === 'Database') {
            <label><span>{{ i18n.t('points.engine') }}</span>
              <select name="engine" [(ngModel)]="f.engine">
                @for (e of engines; track e) {
                  <option [value]="e">{{ e }}</option>
                }
              </select>
            </label>
            <label><span>{{ i18n.t('points.port') }}</span><input type="number" name="port" [(ngModel)]="f.port" min="1" max="65535" /></label>
            <small>{{ i18n.t('points.secretHint') }}</small>
          }
          <label><span>{{ i18n.t('points.interval') }}</span><input type="number" name="intervalSeconds" [(ngModel)]="f.intervalSeconds" min="5" max="86400" required /></label>
          <label><span>{{ i18n.t('points.alertLevel') }}</span>
            <select name="alertLevel" [(ngModel)]="f.alertLevel">
              <option value="Problem">{{ i18n.t('points.level.Problem') }}</option>
              <option value="Warning">{{ i18n.t('points.level.Warning') }}</option>
              <option value="Unknown">{{ i18n.t('points.level.Unknown') }}</option>
            </select>
          </label>
          <label class="inline"><input type="checkbox" name="enabled" [(ngModel)]="f.enabled" /> <span>{{ i18n.t('points.enabled') }}</span></label>
          <label class="inline"><input type="checkbox" name="showInShortcut" [(ngModel)]="f.showInShortcut" /> <span>{{ i18n.t('points.shortcut') }}</span></label>
          @if (formError()) {
            <p class="field-error" role="alert">{{ formError() }}</p>
          }
        </form>
      }
      <div drawerActions>
        <button type="button" mcButton="secondary" (click)="form.set(null)">{{ i18n.t('common.cancel') }}</button>
        <button type="submit" form="point-form" mcButton="primary-solid" [disabled]="busy()" data-testid="save-point">{{ i18n.t('common.save') }}</button>
      </div>
    </mc-drawer>
  `,
  styles: `
    .target { font-family: var(--mc-font-mono); font-size: var(--mc-fs-sm); overflow-wrap: anywhere; }
    .muted { color: var(--mc-text-muted); }
    .inline { flex-direction: row; align-items: center; gap: var(--mc-space-2); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DeviceMonitorPointsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly context = inject(DeviceContext);
  private readonly alerts = inject(AlertsApi);
  private readonly api = inject(ConfigurationApi);
  private readonly auth = inject(AuthService);

  protected readonly types = TYPES;
  protected readonly engines = ENGINES;
  protected readonly points = signal<MonitorPoint[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly form = signal<PointForm | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected readonly canEdit = computed(() => this.auth.hasPermission('monitorpoints.manage'));
  protected readonly locale = computed(() => (this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB'));

  protected readonly columns = computed<Column[]>(() => [
    { key: 'displayName', label: this.i18n.t('users.name') },
    { key: 'type', label: this.i18n.t('device.point.type') },
    { key: 'target', label: this.i18n.t('device.point.target') },
    { key: 'status', label: this.i18n.t('common.status') },
    { key: 'responseMs', label: this.i18n.t('device.point.response') },
    { key: 'lastCheckedAt', label: this.i18n.t('device.point.lastChecked') },
    { key: 'intervalSeconds', label: this.i18n.t('points.interval') },
    { key: 'enabled', label: this.i18n.t('points.enabled') },
    { key: 'actions', label: '', width: '150px' },
  ]);

  constructor() {
    effect(() => {
      if (this.context.id()) {
        untracked(() => void this.load());
      }
    });
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);
    try {
      this.points.set(await firstValueFrom(this.alerts.monitorPoints(this.context.id())));
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected p(row: unknown): MonitorPoint {
    return row as MonitorPoint;
  }

  protected seen(at: string | null | undefined): string {
    return relativeTime(this.i18n, at);
  }

  protected placeholder(type: string): string {
    return { Website: 'https://', Ping: '198.51.100.10', Database: 'localhost/ORGDb', Disk: 'C:', Service: 'W3SVC', Application: 'app.exe' }[type] ?? '';
  }

  protected open(point: MonitorPoint | null): void {
    this.formError.set(null);
    const settings = (point?.settings ?? {}) as Record<string, unknown>;
    this.form.set({
      id: point?.id ?? null,
      version: point?.version ?? '',
      key: point?.key ?? '',
      displayName: point?.displayName ?? '',
      type: point?.type ?? 'Website',
      target: point?.target ?? '',
      intervalSeconds: point?.intervalSeconds || 60,
      alertLevel: point?.alertLevel ?? 'Problem',
      enabled: point?.enabled ?? true,
      showInShortcut: point?.showInShortcut ?? true,
      expectStatus: typeof settings['expectStatus'] === 'number' ? settings['expectStatus'] : null,
      engine: typeof settings['engine'] === 'string' ? settings['engine'] : 'SqlServer',
      port: typeof settings['port'] === 'number' ? settings['port'] : null,
    });
  }

  protected async save(): Promise<void> {
    const f = this.form();
    if (!f) {
      return;
    }
    const settings: Record<string, unknown> =
      f.type === 'Website' && f.expectStatus ? { expectStatus: Number(f.expectStatus) } : f.type === 'Database' ? { engine: f.engine, ...(f.port ? { port: Number(f.port) } : {}) } : {};
    const body: MonitorPointInput = {
      key: f.id ? null : f.key.trim() || null,
      displayName: f.displayName,
      type: f.type,
      target: f.target,
      intervalSeconds: Number(f.intervalSeconds),
      alertLevel: f.alertLevel,
      enabled: f.enabled,
      showInShortcut: f.showInShortcut,
      settings: Object.keys(settings).length ? (settings as MonitorPointInput['settings']) : null,
    };
    this.busy.set(true);
    this.formError.set(null);
    try {
      if (f.id) {
        await firstValueFrom(this.api.updatePoint(this.context.id(), f.id, body, f.version));
      } else {
        await firstValueFrom(this.api.addPoint(this.context.id(), body));
      }
      this.form.set(null);
      await this.load();
    } catch (e) {
      this.formError.set(this.i18n.errorMessage(toProblem(e).code));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(point: MonitorPoint): Promise<void> {
    try {
      await firstValueFrom(this.api.deletePoint(this.context.id(), point.id));
      this.points.update((list) => list.filter((p) => p.id !== point.id));
    } catch {
      // The error toast explains why.
    }
  }
}
