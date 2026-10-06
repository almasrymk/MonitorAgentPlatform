import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';

import { UserListItem } from '../../core/api/models';
import { I18nService } from '../../core/i18n/i18n.service';
import { Avatar } from '../../shared/ui/avatar';
import { Button } from '../../shared/ui/button';
import { CellDef, Column, DataTable } from '../../shared/ui/data-table';
import { StatusPill } from '../../shared/ui/status-pill';

export type UserAction = 'edit' | 'activate' | 'deactivate' | 'resend';

/** Users table shared by Users & Permissions and Users & Roles (07 section 5.8). */
@Component({
  selector: 'mc-users-table',
  imports: [DataTable, CellDef, Avatar, StatusPill, Button],
  template: `
    <mc-data-table [columns]="columns()" [rows]="users()" [loading]="loading()" [emptyText]="i18n.t('users.empty')" [sort]="sort()" (sortChange)="sortChange.emit($event)">
      <ng-template mcCell="name" let-row>
        <span class="who"><mc-avatar [name]="$any(row).fullName" [size]="28" /> {{ $any(row).fullName }}</span>
      </ng-template>
      <ng-template mcCell="role" let-row><mc-status-pill status="info" [label]="i18n.t('role.' + $any(row).role)" /></ng-template>
      <ng-template mcCell="permissions" let-row>{{ i18n.t('permissionSummary.' + $any(row).role) }}</ng-template>
      <ng-template mcCell="status" let-row><mc-status-pill [status]="$any(row).status" /></ng-template>
      <ng-template mcCell="lastLogin" let-row>{{ lastLogin($any(row)) }}</ng-template>
      <ng-template mcCell="actions" let-row>
        <span class="actions">
          @if (editable()) {
            <button type="button" mcButton="ghost" size="sm" (click)="action.emit({ action: 'edit', user: $any(row) })">{{ i18n.t('common.edit') }}</button>
          }
          @if ($any(row).status === 'Inactive') {
            <button type="button" mcButton="ghost" size="sm" (click)="action.emit({ action: 'activate', user: $any(row) })">{{ i18n.t('users.activate') }}</button>
          } @else {
            <button type="button" mcButton="ghost" size="sm" data-testid="deactivate" (click)="action.emit({ action: 'deactivate', user: $any(row) })">{{ i18n.t('users.deactivate') }}</button>
          }
          @if ($any(row).status === 'Invited' && editable()) {
            <button type="button" mcButton="ghost" size="sm" (click)="action.emit({ action: 'resend', user: $any(row) })">{{ i18n.t('users.resend') }}</button>
          }
        </span>
      </ng-template>
    </mc-data-table>
  `,
  styles: `
    .who { display: inline-flex; align-items: center; gap: var(--mc-space-2); font-weight: var(--mc-fw-medium); }
    .actions { display: inline-flex; gap: var(--mc-space-1); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsersTable {
  protected readonly i18n = inject(I18nService);
  readonly users = input.required<UserListItem[]>();
  readonly loading = input(false);
  readonly sort = input<string | null>('name');
  readonly editable = input(true);
  readonly sortChange = output<string | null>();
  readonly action = output<{ action: UserAction; user: UserListItem }>();

  protected readonly columns = computed<Column[]>(() => [
    { key: 'name', label: this.i18n.t('users.name'), sortable: true },
    { key: 'email', label: this.i18n.t('users.email'), sortable: true },
    { key: 'role', label: this.i18n.t('users.role'), sortable: true },
    { key: 'permissions', label: this.i18n.t('users.permissions') },
    { key: 'status', label: this.i18n.t('common.status'), sortable: true },
    { key: 'lastLogin', label: this.i18n.t('users.lastLogin'), sortable: true },
    { key: 'actions', label: '' },
  ]);

  protected lastLogin(user: UserListItem): string {
    return user.lastLoginAt
      ? new Date(user.lastLoginAt).toLocaleString(this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB', { dateStyle: 'medium', timeStyle: 'short' })
      : this.i18n.t('users.never');
  }
}
