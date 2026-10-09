import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { PlatformUsersApi } from '../../core/api/api.services';
import { Paged, UserListItem } from '../../core/api/models';
import { toProblem } from '../../core/api/problem';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { Button } from '../../shared/ui/button';
import { Dialog } from '../../shared/ui/dialog';
import { PageHeader } from '../../shared/ui/headers';
import { Icon } from '../../shared/ui/icon';
import { Pagination } from '../../shared/ui/pagination';
import { ErrorState } from '../../shared/ui/states';
import { UserAction, UsersTable } from './users-table';

/** Users & Roles (platform staff). */
@Component({
  selector: 'mc-platform-users-page',
  imports: [FormsModule, RouterLink, PageHeader, UsersTable, Pagination, Dialog, Button, Icon, ErrorState],
  template: `
    <mc-page-header [title]="i18n.t('platformUsers.title')" [subtitle]="i18n.t('platformUsers.subtitle')">
      @if (auth.hasPermission('platform.audit.read')) {
        <a mcButton="secondary" routerLink="/admin/audit" data-testid="open-audit"><mc-icon name="shield" [size]="16" /> {{ i18n.t('audit.title') }}</a>
      }
      <button type="button" mcButton="primary-solid" (click)="openNew()"><mc-icon name="plus" [size]="16" /> {{ i18n.t('users.add') }}</button>
    </mc-page-header>
    @if (failed()) {
      <mc-error-state (retry)="load()" />
    } @else {
      <mc-users-table [users]="result()?.items ?? []" [loading]="loading()" [editable]="false" [sort]="sort()" (sortChange)="sort.set($event ?? 'name'); load()" (action)="onAction($event.action, $event.user)" />
      @if (result(); as r) {
        <mc-pagination [page]="r.page" [pageSize]="r.pageSize" [total]="r.total" (pageChange)="page.set($event); load()" />
      }
    }
    <mc-dialog [open]="creating()" (openChange)="creating.set($event)" [title]="i18n.t('users.add')">
      <form class="mc-form" id="platform-user-form" (ngSubmit)="save()">
        <label><span>{{ i18n.t('users.name') }}</span><input name="fullName" [(ngModel)]="fullName" required /></label>
        <label><span>{{ i18n.t('users.email') }}</span><input name="email" type="email" [(ngModel)]="email" required /></label>
        <label><span>{{ i18n.t('users.role') }}</span>
          <select name="role" [(ngModel)]="role">
            <option value="PlatformSupport">{{ i18n.t('role.PlatformSupport') }}</option>
            <option value="PlatformAdmin">{{ i18n.t('role.PlatformAdmin') }}</option>
          </select>
        </label>
        <label><span>{{ i18n.t('login.password') }}</span><input name="password" type="password" autocomplete="new-password" [(ngModel)]="password" required /></label>
        @if (formError()) {
          <p class="field-error" role="alert">{{ formError() }}</p>
        }
      </form>
      <div dialogActions>
        <button type="button" mcButton="secondary" (click)="creating.set(false)">{{ i18n.t('common.cancel') }}</button>
        <button type="submit" form="platform-user-form" mcButton="primary-solid" [disabled]="busy()">{{ i18n.t('common.save') }}</button>
      </div>
    </mc-dialog>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlatformUsersPage {
  protected readonly i18n = inject(I18nService);
  protected readonly auth = inject(AuthService);
  private readonly api = inject(PlatformUsersApi);

  protected readonly sort = signal('name');
  protected readonly page = signal(1);
  protected readonly result = signal<Paged<UserListItem> | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly creating = signal(false);
  protected readonly busy = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected fullName = '';
  protected email = '';
  protected role = 'PlatformSupport';
  protected password = '';

  constructor() {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);
    try {
      this.result.set(await firstValueFrom(this.api.list({ sort: this.sort(), page: this.page(), pageSize: 20 })));
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected openNew(): void {
    this.fullName = '';
    this.email = '';
    this.password = '';
    this.role = 'PlatformSupport';
    this.formError.set(null);
    this.creating.set(true);
  }

  async onAction(action: UserAction, user: UserListItem): Promise<void> {
    if (action === 'activate') {
      await firstValueFrom(this.api.activate(user.id));
    } else if (action === 'deactivate') {
      await firstValueFrom(this.api.deactivate(user.id));
    }
    await this.load();
  }

  async save(): Promise<void> {
    this.busy.set(true);
    this.formError.set(null);
    try {
      await firstValueFrom(this.api.create({ fullName: this.fullName, email: this.email, role: this.role, password: this.password }));
      this.creating.set(false);
      await this.load();
    } catch (e) {
      const problem = toProblem(e);
      const firstFieldError = problem.errors ? Object.values(problem.errors)[0]?.[0] : undefined;
      this.formError.set(firstFieldError ?? this.i18n.errorMessage(problem.code));
    } finally {
      this.busy.set(false);
    }
  }
}
