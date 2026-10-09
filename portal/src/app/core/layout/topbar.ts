import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { Avatar } from '../../shared/ui/avatar';
import { Icon } from '../../shared/ui/icon';
import { Select } from '../../shared/ui/select';
import { AuthService } from '../auth/auth.service';
import { I18nService, Language } from '../i18n/i18n.service';
import { DateRange, DateRangeStore } from '../state/date-range.store';
import { NotificationsStore } from '../state/notifications.store';

/**
 * Top bar: brand, search (customers on the platform, devices inside a customer; Enter opens the filtered list), date range,
 * bell with the unread count, user menu with language and sign-out.
 */
@Component({
  selector: 'mc-topbar',
  imports: [Avatar, Icon, Select, RouterLink],
  template: `
    <div class="brand">
      <span class="brand-mark" aria-hidden="true"></span>
      <span class="brand-name">{{ i18n.t('app.name') }}</span>
    </div>
    <label class="search">
      <mc-icon name="search" [size]="16" />
      <input type="search" [attr.placeholder]="bell.platform() ? i18n.t('topbar.searchCustomers') : i18n.t('topbar.searchDevices')" [attr.aria-label]="i18n.t('topbar.search')"
        (keydown.enter)="search($any($event.target))" data-testid="topbar-search" />
    </label>
    <div class="right">
      <mc-select [label]="i18n.t('topbar.dateRange')" [options]="rangeOptions()" [value]="dates.range()" (valueChange)="dates.range.set($any($event))" />
      <a class="bell" [routerLink]="[bell.root(), 'notifications']" [attr.aria-label]="i18n.t('topbar.notificationsUnread', { n: bell.unread() })" data-testid="bell">
        <mc-icon name="bell" />
        @if (bell.unread() > 0) {
          <span class="badge" data-testid="bell-count">{{ bell.unread() > 99 ? '99+' : bell.unread() }}</span>
        }
      </a>
      <div class="user">
        <button type="button" class="user-button" [attr.aria-expanded]="menuOpen()" aria-haspopup="menu" data-testid="user-menu" (click)="menuOpen.set(!menuOpen())">
          <mc-avatar [name]="auth.user()?.fullName ?? '?'" [size]="30" />
          <span class="user-text">
            <span class="user-name">{{ auth.user()?.fullName }}</span>
            <span class="user-role">{{ i18n.t('role.' + (auth.user()?.role ?? '')) }}</span>
          </span>
          <mc-icon name="chevronDown" [size]="14" />
        </button>
        @if (menuOpen()) {
          <div class="menu" role="menu">
            <button type="button" role="menuitem" data-testid="language-switch" (click)="switchLanguage()">
              <mc-icon name="globe" [size]="16" /> {{ i18n.language() === 'en' ? i18n.t('language.ar') : i18n.t('language.en') }}
            </button>
            <button type="button" role="menuitem" data-testid="sign-out" (click)="auth.logout()">
              <mc-icon name="logout" [size]="16" /> {{ i18n.t('topbar.signOut') }}
            </button>
          </div>
        }
      </div>
    </div>
  `,
  styles: `
    :host { display: flex; align-items: center; gap: var(--mc-space-5); padding-inline: var(--mc-space-5); background: var(--mc-bg-topbar); border-block-end: 1px solid var(--mc-border); position: sticky; inset-block-start: 0; z-index: var(--mc-z-topbar); }
    .brand { display: flex; align-items: center; gap: var(--mc-space-2); inline-size: calc(var(--mc-sidebar-width) - var(--mc-space-5)); font-weight: var(--mc-fw-bold); font-size: var(--mc-fs-lg); }
    .brand-mark { inline-size: 22px; block-size: 22px; border-radius: var(--mc-radius-sm); background: var(--mc-brand); }
    .search { flex: 1; max-inline-size: 420px; display: flex; align-items: center; gap: var(--mc-space-2); padding-inline: var(--mc-space-3); block-size: 36px; background: var(--mc-bg-input); border: 1px solid var(--mc-border); border-radius: var(--mc-radius-md); color: var(--mc-text-muted); }
    .search input { flex: 1; background: transparent; border: 0; color: var(--mc-text); font: inherit; }
    .right { margin-inline-start: auto; display: flex; align-items: center; gap: var(--mc-space-3); }
    .bell { position: relative; background: none; border: 0; color: var(--mc-text-secondary); cursor: pointer; display: inline-flex; padding: 6px; border-radius: var(--mc-radius-md); }
    .bell:hover { background: var(--mc-bg-hover); }
    .badge { position: absolute; inset-block-start: 0; inset-inline-end: 0; min-inline-size: 16px; block-size: 16px; padding-inline: 4px; border-radius: var(--mc-radius-pill); background: var(--mc-danger); color: var(--mc-text); font-size: 10px; font-weight: var(--mc-fw-bold); display: inline-flex; align-items: center; justify-content: center; line-height: 1; }
    .user { position: relative; }
    .user-button { display: flex; align-items: center; gap: var(--mc-space-2); background: none; border: 0; color: var(--mc-text); cursor: pointer; font: inherit; padding: 4px; border-radius: var(--mc-radius-md); }
    .user-text { display: flex; flex-direction: column; align-items: flex-start; line-height: 1.2; }
    .user-name { font-weight: var(--mc-fw-medium); }
    .user-role { color: var(--mc-text-muted); font-size: var(--mc-fs-xs); }
    .menu { position: absolute; inset-inline-end: 0; inset-block-start: calc(100% + 6px); min-inline-size: 180px; background: var(--mc-bg-card-raised); border: 1px solid var(--mc-border-strong); border-radius: var(--mc-radius-md); padding: var(--mc-space-1); display: flex; flex-direction: column; }
    .menu button { display: flex; align-items: center; gap: var(--mc-space-2); background: none; border: 0; color: var(--mc-text); padding: var(--mc-space-2) var(--mc-space-3); border-radius: var(--mc-radius-sm); cursor: pointer; font: inherit; text-align: start; }
    .menu button:hover { background: var(--mc-bg-hover); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Topbar {
  protected readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthService);
  protected readonly dates = inject(DateRangeStore);
  protected readonly bell = inject(NotificationsStore);
  protected readonly menuOpen = signal(false);
  private readonly router = inject(Router);

  /** Opens Customers (platform) or Devices (customer area / workspace) filtered by the text. */
  protected search(input: HTMLInputElement): void {
    const text = input.value.trim();
    if (!text) {
      return;
    }
    const root = this.bell.root();
    void this.router.navigate(this.bell.platform() ? ['/admin/customers'] : [root, 'devices'], { queryParams: { search: text } });
    input.value = '';
  }

  protected rangeOptions() {
    return this.dates.options.map((range: DateRange) => ({ value: range, label: this.i18n.t(`range.${range}`) }));
  }

  protected async switchLanguage(): Promise<void> {
    const next: Language = this.i18n.language() === 'en' ? 'ar' : 'en';
    this.menuOpen.set(false);
    await this.auth.setLanguage(next);
  }
}
