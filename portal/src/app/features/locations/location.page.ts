import { ChangeDetectionStrategy, Component, computed, effect, inject, input, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { debounceTime } from 'rxjs';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { BreadcrumbLabels } from '../../core/layout/breadcrumb-labels';
import { LiveService } from '../../core/live/live.service';
import { EntityHeader } from '../../shared/ui/headers';
import { Skeleton } from '../../shared/ui/skeleton';
import { ErrorState } from '../../shared/ui/states';
import { LocationContext } from './location-context';

/** A location: entity header and the Overview, Devices, Notifications, Archive, Reports and Settings tabs (07 sections 3, 5.5, 5.6 and 5.8). */
@Component({
  selector: 'mc-location-page',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, EntityHeader, Skeleton, ErrorState],
  providers: [LocationContext],
  template: `
    @if (context.failed()) {
      <mc-error-state (retry)="context.load(id())" />
    } @else if (context.dashboard(); as d) {
      <mc-entity-header icon="location" [title]="d.location.isDefault ? i18n.t('locations.unassigned') : d.location.name" [status]="online() ? 'online' : 'offline'" [meta]="meta()" />
    } @else {
      <mc-skeleton [height]="64" />
    }
    <nav class="tabs" [attr.aria-label]="i18n.t('locations.tabs')">
      <a routerLink="overview" routerLinkActive="active" ariaCurrentWhenActive="page" data-testid="tab-overview">{{ i18n.t('locations.tabOverview') }}</a>
      <a routerLink="devices" routerLinkActive="active" ariaCurrentWhenActive="page" data-testid="tab-devices">{{ i18n.t('locations.tabDevices') }}</a>
      <a routerLink="notifications" routerLinkActive="active" ariaCurrentWhenActive="page" data-testid="tab-notifications">{{ i18n.t('nav.notifications') }}</a>
      @if (auth.hasPermission('archive.read')) {
        <a routerLink="archive" routerLinkActive="active" ariaCurrentWhenActive="page" data-testid="tab-archive">{{ i18n.t('nav.archive') }}</a>
      }
      @if (auth.hasPermission('reports.read')) {
        <a routerLink="reports" routerLinkActive="active" ariaCurrentWhenActive="page" data-testid="tab-reports">{{ i18n.t('nav.reports') }}</a>
      }
      <a routerLink="settings" routerLinkActive="active" ariaCurrentWhenActive="page" data-testid="tab-settings">{{ i18n.t('nav.settings') }}</a>
    </nav>
    <router-outlet />
  `,
  styles: `
    :host { display: block; }
    .tabs { display: flex; gap: var(--mc-space-5); border-block-end: 1px solid var(--mc-border); margin-block-end: var(--mc-space-4); }
    .tabs a { padding: var(--mc-space-2) 0; color: var(--mc-text-muted); text-decoration: none; border-block-end: 2px solid transparent; margin-block-end: -1px; }
    .tabs a.active { color: var(--mc-text); border-block-end-color: var(--mc-brand); font-weight: var(--mc-fw-medium); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LocationPage {
  protected readonly i18n = inject(I18nService);
  protected readonly context = inject(LocationContext);
  protected readonly router = inject(Router);
  protected readonly auth = inject(AuthService);
  readonly id = input.required<string>();

  protected readonly online = computed(() => (this.context.dashboard()?.tiles.find((t) => t.key === 'online')?.value ?? 0) > 0);
  protected readonly meta = computed(() => {
    const d = this.context.dashboard();
    if (!d) {
      return [];
    }
    const l = d.location;
    const since = new Date(l.customerSince).toLocaleDateString(this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB', { month: 'short', year: 'numeric' });
    return [
      l.customerName,
      [l.city, l.country].filter((p) => !!p).join(', ') || '—',
      l.planName ?? this.i18n.t('customers.planPending'),
      this.i18n.t('dashboard.devicesCount', { n: d.tiles.find((t) => t.key === 'devices')?.value ?? 0 }),
      this.i18n.t('dashboard.customerSince', { date: since }),
    ];
  });

  constructor() {
    effect(() => void this.context.load(this.id()));
    const live = inject(LiveService);
    effect((onCleanup) => {
      const id = this.id();
      onCleanup(untracked(() => live.subscribe({ kind: 'location', id })));
    });
    live.summary$.pipe(takeUntilDestroyed()).subscribe((event) => {
      if (event.scope === 'location' && event.id === this.id()) {
        void this.context.load(this.id(), true);
      }
    });
    live.alert$.pipe(debounceTime(1500), takeUntilDestroyed()).subscribe((event) => {
      if (event.locationId === this.id()) {
        void this.context.load(this.id(), true);
      }
    });
    const labels = inject(BreadcrumbLabels);
    effect(() => {
      const l = this.context.dashboard()?.location;
      if (l) {
        labels.set(':location', l.isDefault ? this.i18n.t('locations.unassigned') : l.name);
      }
    });
  }
}
