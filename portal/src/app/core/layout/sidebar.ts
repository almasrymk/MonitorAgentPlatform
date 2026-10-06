import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

import { Icon } from '../../shared/ui/icon';
import { AuthService } from '../auth/auth.service';
import { I18nService } from '../i18n/i18n.service';
import { APP_VERSION } from '../version';
import { NavItem } from './navigation';

/** Contextual sidebar: items the user may not use are not rendered (and their routes are guarded). */
@Component({
  selector: 'mc-sidebar',
  imports: [RouterLink, RouterLinkActive, Icon],
  template: `
    <nav [attr.aria-label]="i18n.t('shell.navigation')" data-testid="sidebar">
      @if (backLink()) {
        <a class="back" [routerLink]="backLink()!.path" data-testid="sidebar-back">
          <mc-icon name="chevronLeft" [size]="16" /> {{ i18n.t(backLink()!.key) }}
        </a>
      }
      <ul>
        @for (item of visible(); track item.key) {
          <li>
            <a [routerLink]="base() + '/' + item.path" routerLinkActive="active" [attr.data-testid]="'nav-' + item.path">
              <mc-icon [name]="item.icon" />
              <span>{{ i18n.t(item.key) }}</span>
            </a>
          </li>
        }
      </ul>
    </nav>
    <p class="footer" data-testid="version">{{ i18n.t('app.version', { version: version }) }}</p>
  `,
  styles: `
    :host { display: flex; flex-direction: column; justify-content: space-between; background: var(--mc-bg-sidebar); border-inline-end: 1px solid var(--mc-border); padding: var(--mc-space-3); min-block-size: 0; }
    ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 2px; }
    a { display: flex; align-items: center; gap: var(--mc-space-3); padding: var(--mc-space-2) var(--mc-space-3); border-radius: var(--mc-radius-md); color: var(--mc-text-secondary); }
    a:hover { background: var(--mc-bg-hover); color: var(--mc-text); }
    a.active { background: var(--mc-bg-nav-active); color: var(--mc-brand); font-weight: var(--mc-fw-medium); }
    .back { color: var(--mc-text-muted); margin-block-end: var(--mc-space-3); font-size: var(--mc-fs-sm); }
    :host-context([dir='rtl']) .back mc-icon { transform: scaleX(-1); }
    .footer { margin: 0; color: var(--mc-text-faint); font-size: var(--mc-fs-xs); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Sidebar {
  protected readonly i18n = inject(I18nService);
  private readonly auth = inject(AuthService);
  readonly items = input.required<NavItem[]>();
  /** Absolute base path of the context, e.g. `/admin` or `/admin/customers/{id}`. */
  readonly base = input.required<string>();
  readonly backLink = input<{ key: string; path: string } | null>(null);
  protected readonly version = APP_VERSION;

  protected readonly visible = computed(() => {
    const permissions = this.auth.permissions();
    return this.items().filter((item) => !item.permission || permissions.has(item.permission));
  });
}
