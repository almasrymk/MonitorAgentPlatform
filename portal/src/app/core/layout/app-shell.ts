import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';

import { ToastHost } from '../../shared/ui/toast-host';
import { AuthService } from '../auth/auth.service';
import { I18nService } from '../i18n/i18n.service';
import { ScopeStore } from '../state/scope.store';
import { Breadcrumb } from './breadcrumb';
import { CUSTOMER_MENU, NavItem, PLATFORM_MENU, DEVICE_MENU, LOCATION_MENU } from './navigation';
import { Sidebar } from './sidebar';
import { Topbar } from './topbar';
import { WorkspaceBanner } from './workspace-banner';

/** The page frame: top bar, workspace banner, contextual sidebar, breadcrumb and content. */
@Component({
  selector: 'mc-app-shell',
  imports: [RouterOutlet, Topbar, Sidebar, Breadcrumb, WorkspaceBanner, ToastHost],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AppShell {
  protected readonly i18n = inject(I18nService);
  private readonly auth = inject(AuthService);
  private readonly scope = inject(ScopeStore);
  private readonly router = inject(Router);
  private readonly url = signal(this.router.url);

  constructor() {
    this.router.events
      .pipe(filter((e) => e instanceof NavigationEnd), takeUntilDestroyed())
      .subscribe((e) => this.url.set((e as NavigationEnd).urlAfterRedirects));
  }

  /** Which sidebar the current route shows. */
  protected readonly context = computed<{ items: NavItem[]; base: string; back: { key: string; path: string } | null }>(() => {
    const url = this.url().split(/[?#]/)[0];
    const workspace = this.scope.workspace();
    // A location or a device has its own menu with a back link (07 section 3).
    const inner = /^((?:\/app|\/admin\/customers\/[^/]+))\/(locations|devices)\/([^/]+)/.exec(url);
    if (inner && (inner[1] === '/app' || workspace)) {
      const [, area, kind, id] = inner;
      return kind === 'locations'
        ? { items: LOCATION_MENU, base: `${area}/locations/${id}`, back: { key: 'nav.locations', path: `${area}/locations` } }
        : { items: DEVICE_MENU, base: `${area}/devices/${id}`, back: { key: 'nav.devices', path: `${area}/devices` } };
    }
    if (url.startsWith('/admin/customers/') && workspace) {
      return { items: CUSTOMER_MENU, base: `/admin/customers/${workspace.id}`, back: { key: 'nav.customers', path: '/admin/customers' } };
    }
    if (url.startsWith('/app')) {
      return { items: CUSTOMER_MENU, base: '/app', back: null };
    }
    if (this.auth.isPlatform()) {
      return { items: PLATFORM_MENU, base: '/admin', back: null };
    }
    return { items: CUSTOMER_MENU, base: '/app', back: null };
  });
}
