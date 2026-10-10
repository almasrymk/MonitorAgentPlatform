import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';

import { Paged } from '../../core/api/models';
import { NotificationItem, NotificationsApi } from '../../core/api/monitoring.api';
import { I18nService } from '../../core/i18n/i18n.service';
import { LiveService } from '../../core/live/live.service';
import { NotificationsStore } from '../../core/state/notifications.store';
import { Button } from '../../shared/ui/button';
import { Card } from '../../shared/ui/card';
import { CellDef, Column, DataTable } from '../../shared/ui/data-table';
import { PageHeader } from '../../shared/ui/headers';
import { Pagination } from '../../shared/ui/pagination';
import { Select } from '../../shared/ui/select';
import { ErrorState } from '../../shared/ui/states';
import { StatusPill } from '../../shared/ui/status-pill';
import { LocationContext } from '../locations/location-context';

const PAGE_SIZE = 20;

/**
 * Notifications (07 section 5.8): the platform feed in `/admin`, the customer feed in `/app` and in a workspace, and
 * the feed of one location as a location tab. Filters: severity and period; mark one or all as read.
 */
@Component({
  selector: 'mc-notifications-page',
  imports: [DatePipe, PageHeader, Card, DataTable, CellDef, StatusPill, Select, Button, Pagination, ErrorState],
  template: `
    @if (!location) {
      <mc-page-header [title]="i18n.t('nav.notifications')" [subtitle]="i18n.t('notifications.subtitle')" />
    }
    <mc-card [flush]="true">
      <div class="toolbar">
        <mc-select [label]="i18n.t('notifications.severity')" [options]="severities()" [value]="severity()" (valueChange)="setSeverity($event)" data-testid="severity-filter" />
        <mc-select [label]="i18n.t('dashboard.period')" [options]="periods()" [value]="days()" (valueChange)="setDays($event)" />
        <span class="spacer"></span>
        <button type="button" mcButton="secondary" size="sm" [disabled]="bell.unread() === 0 && !hasUnread()" (click)="markAll()" data-testid="mark-all-read">
          {{ i18n.t('notifications.markAllRead') }}
        </button>
      </div>
      @if (failed()) {
        <mc-error-state (retry)="load()" />
      } @else {
        <mc-data-table [columns]="columns()" [rows]="page()?.items ?? []" [loading]="loading()" [emptyText]="i18n.t('notifications.empty')" data-testid="notifications">
          <ng-template mcCell="createdAt" let-row>
            <span class="time" [class.unread]="!n(row).read">
              @if (!n(row).read) {
                <span class="dot" role="img" [attr.aria-label]="i18n.t('notifications.unread')"></span>
              }
              {{ n(row).createdAt | date: 'short' : undefined : locale() }}
            </span>
          </ng-template>
          <ng-template mcCell="severity" let-row><mc-status-pill [status]="n(row).severity" [label]="i18n.t('severity.' + n(row).severity.toLowerCase())" /></ng-template>
          <ng-template mcCell="source" let-row>{{ platform() ? (n(row).customerName ?? i18n.t('notifications.platform')) : (n(row).deviceName ?? '—') }}</ng-template>
          <ng-template mcCell="message" let-row>
            <div class="message" [class.unread]="!n(row).read">
              <strong>{{ n(row).title }}</strong>
              <span class="muted">{{ n(row).body }}</span>
            </div>
          </ng-template>
          <ng-template mcCell="category" let-row>{{ i18n.t('category.' + n(row).category.toLowerCase()) }}</ng-template>
          <ng-template mcCell="actions" let-row>
            @if (!n(row).read) {
              <button type="button" mcButton="ghost" size="sm" (click)="markRead(n(row))" [attr.data-testid]="'read-' + n(row).id">{{ i18n.t('notifications.markRead') }}</button>
            }
          </ng-template>
        </mc-data-table>
        @if ((page()?.total ?? 0) > pageSize) {
          <mc-pagination [page]="pageNumber()" [pageSize]="pageSize" [total]="page()?.total ?? 0" (pageChange)="goTo($event)" />
        }
      }
    </mc-card>
  `,
  styles: `
    :host { display: block; }
    .toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: var(--mc-space-3); padding: var(--mc-space-4); }
    .spacer { flex: 1; }
    .time { display: inline-flex; align-items: center; gap: 6px; white-space: nowrap; color: var(--mc-text-secondary); }
    .dot { inline-size: 8px; block-size: 8px; border-radius: 50%; background: var(--mc-info); }
    .message { display: flex; flex-direction: column; gap: 2px; }
    .message:not(.unread) strong { font-weight: var(--mc-fw-medium); }
    .muted { color: var(--mc-text-muted); font-size: var(--mc-fs-sm); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotificationsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly bell = inject(NotificationsStore);
  private readonly api = inject(NotificationsApi);
  protected readonly location = inject(LocationContext, { optional: true });
  protected readonly pageSize = PAGE_SIZE;

  protected readonly platform = computed(() => this.bell.platform() && !this.location);
  protected readonly severity = signal('');
  protected readonly days = signal('7');
  protected readonly pageNumber = signal(1);
  protected readonly page = signal<Paged<NotificationItem> | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly hasUnread = computed(() => (this.page()?.items ?? []).some((i) => !i.read));
  protected readonly locale = computed(() => (this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB'));

  protected readonly severities = computed(() => [
    { value: '', label: this.i18n.t('notifications.allSeverities') },
    ...['critical', 'warning', 'info'].map((s) => ({ value: s, label: this.i18n.t(`severity.${s}`) })),
  ]);

  protected readonly periods = computed(() => [7, 30, 90].map((d) => ({ value: String(d), label: this.i18n.t('dashboard.lastDays', { n: d }) })));

  protected readonly columns = computed<Column[]>(() => [
    { key: 'createdAt', label: this.i18n.t('alerts.time'), width: '170px' },
    { key: 'severity', label: this.i18n.t('dashboard.severity'), width: '120px' },
    { key: 'source', label: this.platform() ? this.i18n.t('alerts.customer') : this.i18n.t('alerts.device') },
    { key: 'message', label: this.i18n.t('alerts.message') },
    { key: 'category', label: this.i18n.t('notifications.category') },
    { key: 'actions', label: '', width: '120px' },
  ]);

  constructor() {
    effect(() => {
      // Reload when the location of a location tab changes.
      this.location?.id();
      untracked(() => void this.load());
    });
    inject(LiveService).notification$.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(() => void this.load(true));
  }

  protected n(row: unknown): NotificationItem {
    return row as NotificationItem;
  }

  async load(quiet = false): Promise<void> {
    if (!quiet) {
      this.loading.set(true);
    }
    this.failed.set(false);
    const from = new Date(Date.now() - Number(this.days()) * 86_400_000).toISOString();
    try {
      this.page.set(await firstValueFrom(this.api.list(
        { locationId: this.location?.id() || null, severity: this.severity() || null, from, page: this.pageNumber(), pageSize: PAGE_SIZE },
        this.platform(),
      )));
    } catch {
      if (!quiet) {
        this.failed.set(true);
      }
    } finally {
      this.loading.set(false);
    }
  }

  protected setSeverity(value: string): void {
    this.severity.set(value);
    this.pageNumber.set(1);
    void this.load();
  }

  protected setDays(value: string): void {
    this.days.set(value);
    this.pageNumber.set(1);
    void this.load();
  }

  protected goTo(page: number): void {
    this.pageNumber.set(page);
    void this.load();
  }

  protected async markRead(item: NotificationItem): Promise<void> {
    await firstValueFrom(this.api.markRead({ ids: [item.id] }, this.platform()));
    this.page.update((p) => (p ? { ...p, items: p.items.map((i) => (i.id === item.id ? { ...i, read: true } : i)) } : p));
    await this.bell.refresh();
  }

  protected async markAll(): Promise<void> {
    // A location tab marks only what it shows; the feed screens mark everything.
    const ids = (this.page()?.items ?? []).filter((i) => !i.read).map((i) => i.id);
    if (this.location && ids.length === 0) {
      return;
    }
    await firstValueFrom(this.api.markRead(this.location ? { ids } : { all: true }, this.platform()));
    this.page.update((p) => (p ? { ...p, items: p.items.map((i) => ({ ...i, read: true })) } : p));
    await this.bell.refresh();
  }
}
