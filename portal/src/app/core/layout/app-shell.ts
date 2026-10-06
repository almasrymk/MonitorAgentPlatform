import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';

import { ToastHost } from '../../shared/ui/toast-host';
import { AuthService } from '../auth/auth.service';
import { I18nService } from '../i18n/i18n.service';
import { ScopeStore } from '../state/scope.store';
import { Breadcrumb } from './breadcrumb';
import { CUSTOMER_MENU, NavItem, PLATFORM_MENU } from './navigation';
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
    const url = this.url();
    const workspace = this.scope.workspace();
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
