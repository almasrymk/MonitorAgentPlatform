import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';

import { Alert, MonitorPoint } from '../../../core/api/monitoring.api';
import { I18nService } from '../../../core/i18n/i18n.service';
import { relativeTime } from '../../../shared/format';
import { Icon } from '../../../shared/ui/icon';
import { EmptyState } from '../../../shared/ui/states';
import { StatusPill } from '../../../shared/ui/status-pill';

const TYPE_ICONS: Record<string, string> = {
  Website: 'globe', Database: 'archive', Ping: 'reports', Application: 'devices', Service: 'settings', Disk: 'archive', Network: 'globe', Custom: 'dashboard',
};

/** Monitor Points (07 section 5.7): a carousel of round icons with name and status dot; selecting one shows its detail. Read-only in M6. */
@Component({
  selector: 'mc-monitor-points',
  imports: [Icon, StatusPill, EmptyState],
  template: `
    @if (points().length === 0) {
      <mc-empty-state icon="dashboard" [title]="i18n.t('device.noMonitorPoints')" />
    } @else {
      <div class="carousel" role="listbox" [attr.aria-label]="i18n.t('device.monitorPoints')" data-testid="monitor-points">
        @for (p of points(); track p.id) {
          <button type="button" role="option" class="point" [attr.aria-selected]="selected()?.id === p.id" [attr.data-status]="p.status.toLowerCase()" (click)="toggle(p)" [attr.data-testid]="'point-' + p.key">
            <span class="circle"><mc-icon [name]="icon(p.type)" [size]="20" /><span class="dot" aria-hidden="true"></span></span>
            <span class="name">{{ p.displayName }}</span>
          </button>
        }
      </div>
      @if (selected(); as p) {
        <div class="detail" role="region" [attr.aria-label]="p.displayName" data-testid="point-detail">
          <header><strong>{{ p.displayName }}</strong><mc-status-pill [status]="p.status" /></header>
          <dl>
            <dt>{{ i18n.t('device.point.type') }}</dt><dd>{{ p.type }}</dd>
            <dt>{{ i18n.t('device.point.target') }}</dt><dd>{{ p.target || '—' }}</dd>
            <dt>{{ i18n.t('device.point.message') }}</dt><dd>{{ p.message || '—' }}</dd>
            <dt>{{ i18n.t('device.point.response') }}</dt><dd>{{ p.responseMs !== null && p.responseMs !== undefined ? p.responseMs + ' ms' : '—' }}</dd>
            <dt>{{ i18n.t('device.point.lastChecked') }}</dt><dd>{{ seen(p.lastCheckedAt) }}</dd>
          </dl>
        </div>
      }
    }
  `,
  styles: `
    .carousel { display: flex; gap: var(--mc-space-3); overflow-x: auto; padding-block-end: var(--mc-space-1); }
    .point { display: flex; flex-direction: column; align-items: center; gap: 6px; background: none; border: 0; color: var(--mc-text-secondary); cursor: pointer; font: inherit; font-size: var(--mc-fs-xs); min-inline-size: 72px; padding: 0; }
    .circle { position: relative; inline-size: 48px; block-size: 48px; border-radius: 50%; background: var(--mc-bg-card-raised); display: inline-flex; align-items: center; justify-content: center; border: 2px solid var(--mc-border); }
    .point[aria-selected='true'] .circle { border-color: var(--mc-brand); }
    .dot { position: absolute; inset-block-end: 2px; inset-inline-end: 2px; inline-size: 10px; block-size: 10px; border-radius: 50%; background: var(--mc-neutral); border: 2px solid var(--mc-bg-card); }
    .point[data-status='healthy'] .dot { background: var(--mc-success); }
    .point[data-status='warning'] .dot { background: var(--mc-warning); }
    .point[data-status='critical'] .dot { background: var(--mc-danger); }
    .name { max-inline-size: 80px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .detail { margin-block-start: var(--mc-space-3); padding: var(--mc-space-3); border-radius: var(--mc-radius-md); background: var(--mc-bg-card-raised); }
    .detail header { display: flex; align-items: center; justify-content: space-between; margin-block-end: var(--mc-space-2); }
    .detail dl { display: grid; grid-template-columns: max-content 1fr; gap: 4px var(--mc-space-3); margin: 0; font-size: var(--mc-fs-sm); }
    .detail dt { color: var(--mc-text-muted); }
    .detail dd { margin: 0; overflow-wrap: anywhere; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MonitorPoints {
  protected readonly i18n = inject(I18nService);
  readonly points = input.required<MonitorPoint[]>();
  protected readonly selected = signal<MonitorPoint | null>(null);

  protected icon(type: string): string {
    return TYPE_ICONS[type] ?? 'dashboard';
  }

  protected toggle(point: MonitorPoint): void {
    this.selected.set(this.selected()?.id === point.id ? null : point);
  }

  protected seen(at: string | null | undefined): string {
    return relativeTime(this.i18n, at);
  }
}

/** Messages & Issues Board (07 section 5.7): the device's alerts, open first, with severity, title, description and time. */
@Component({
  selector: 'mc-messages-board',
  imports: [DatePipe, Icon, EmptyState],
  template: `
    @if (items().length === 0) {
      <mc-empty-state icon="bell" [title]="i18n.t('dashboard.noAlerts')" />
    } @else {
      <ul class="board" data-testid="messages-board">
        @for (a of items(); track a.id) {
          <li [attr.data-severity]="a.severity.toLowerCase()" [class.resolved]="a.status === 'Resolved'">
            <mc-icon [name]="a.status === 'Resolved' ? 'check' : 'alert'" [size]="18" />
            <div class="text">
              <strong class="title">{{ a.title }}</strong>
              <span class="description">{{ a.message || i18n.t('category.' + a.category.toLowerCase()) }}</span>
            </div>
            <time [attr.datetime]="a.lastSeenAt" [attr.title]="a.lastSeenAt | date: 'medium' : undefined : locale()">{{ ago(a.lastSeenAt) }}</time>
          </li>
        }
      </ul>
    }
  `,
  styles: `
    .board { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: var(--mc-space-2); }
    li { display: grid; grid-template-columns: auto 1fr auto; gap: var(--mc-space-2); align-items: start; padding: var(--mc-space-2); border-radius: var(--mc-radius-md); background: var(--mc-bg-card-raised); color: var(--mc-info); }
    li[data-severity='critical'] { color: var(--mc-danger); }
    li[data-severity='warning'] { color: var(--mc-warning); }
    li.resolved { color: var(--mc-text-muted); }
    .text { display: flex; flex-direction: column; gap: 2px; min-inline-size: 0; }
    .description { color: var(--mc-text-secondary); font-size: var(--mc-fs-sm); }
    time { color: var(--mc-text-muted); font-size: var(--mc-fs-xs); white-space: nowrap; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MessagesBoard {
  protected readonly i18n = inject(I18nService);
  readonly alerts = input.required<Alert[]>();
  readonly limit = input(5);
  protected readonly locale = computed(() => (this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB'));

  /** Open alerts first (newest first), then the recently resolved ones. */
  protected readonly items = computed(() =>
    [...this.alerts()].sort((a, b) => Number(b.status === 'Open') - Number(a.status === 'Open') || b.lastSeenAt.localeCompare(a.lastSeenAt)).slice(0, this.limit()),
  );

  protected ago(at: string): string {
    return relativeTime(this.i18n, at);
  }
}
