import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter } from 'rxjs';

import { I18nService } from '../i18n/i18n.service';
import { ScopeStore } from '../state/scope.store';
import { BreadcrumbLabels } from './breadcrumb-labels';

interface Crumb {
  label: string;
  url: string | null;
}

interface RawCrumb {
  key: string;
  url: string;
}

/** Built from the `breadcrumb` data of the active routes (`:workspace` = the open customer's name). */
@Component({
  selector: 'mc-breadcrumb',
  imports: [RouterLink],
  template: `
    @if (crumbs().length > 1) {
      <nav [attr.aria-label]="i18n.t('shell.breadcrumb')">
        <ol>
          @for (crumb of crumbs(); track $index) {
            <li>
              @if (crumb.url && !$last) {
                <a [routerLink]="crumb.url">{{ crumb.label }}</a>
              } @else {
                <span [attr.aria-current]="$last ? 'page' : null">{{ crumb.label }}</span>
              }
            </li>
          }
        </ol>
      </nav>
    }
  `,
  styles: `
    ol { display: flex; flex-wrap: wrap; gap: var(--mc-space-2); list-style: none; margin: 0 0 var(--mc-space-3); padding: 0; font-size: var(--mc-fs-sm); color: var(--mc-text-muted); }
    li + li::before { content: '/'; margin-inline-end: var(--mc-space-2); color: var(--mc-text-faint); }
    a { color: var(--mc-text-secondary); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Breadcrumb {
  protected readonly i18n = inject(I18nService);
  private readonly router = inject(Router);
  private readonly scope = inject(ScopeStore);
  private readonly labels = inject(BreadcrumbLabels);
  private readonly raw = signal<RawCrumb[]>([]);
  protected readonly crumbs = computed<Crumb[]>(() => {
    const crumbs: Crumb[] = [];
    for (const { key, url } of this.raw()) {
      const label = key === ':workspace' ? (this.scope.workspace()?.name ?? '') : key.startsWith(':') ? (this.labels.values()[key] ?? '') : this.i18n.t(key);
      if (!crumbs.some((c) => c.label === label)) {
        crumbs.push({ label, url });
      }
    }
    return crumbs;
  });

  constructor() {
    this.update();
    this.router.events
      .pipe(filter((e) => e instanceof NavigationEnd), takeUntilDestroyed())
      .subscribe(() => this.update());
  }

  private update(): void {
    const crumbs: RawCrumb[] = [];
    let route: ActivatedRoute | null = this.router.routerState.root;
    const segments: string[] = [];
    while (route) {
      // During the first activation the snapshot may not exist yet; NavigationEnd updates again.
      const snapshot = route.snapshot as typeof route.snapshot | undefined;
      const urlSegments = snapshot?.url?.map((s) => s.path) ?? [];
      segments.push(...urlSegments);
      const key = snapshot?.data?.['breadcrumb'] as string | undefined;
      if (key && (urlSegments.length > 0 || crumbs.length === 0)) {
        crumbs.push({ key, url: '/' + segments.join('/') });
      }
      route = route.firstChild;
    }
    this.raw.set(crumbs);
  }
}
