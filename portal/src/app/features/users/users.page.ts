import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

import { LocationsApi, UsersApi } from '../../core/api/api.services';
import { LocationCard, Paged, UserListItem } from '../../core/api/models';
import { toProblem } from '../../core/api/problem';
import { I18nService } from '../../core/i18n/i18n.service';
import { ToastService } from '../../core/ui/toast.service';
import { Button } from '../../shared/ui/button';
import { Drawer } from '../../shared/ui/drawer';
import { PageHeader } from '../../shared/ui/headers';
import { Icon } from '../../shared/ui/icon';
import { Pagination } from '../../shared/ui/pagination';
import { SearchInput } from '../../shared/ui/search-input';
import { ErrorState } from '../../shared/ui/states';
import { TabItem, Tabs } from '../../shared/ui/tabs';
import { UserAction, UsersTable } from './users-table';

const ROLES = ['Administrator', 'ITManager', 'Technician', 'ReportViewer'];

interface UserForm {
  id: string | null;
  version: string;
  fullName: string;
  email: string;
  role: string;
  locationIds: string[];
}

/** Users & Permissions (customer, 07 section 5.8). */
@Component({
  selector: 'mc-users-page',
  imports: [FormsModule, PageHeader, Tabs, SearchInput, UsersTable, Pagination, Drawer, Button, Icon, ErrorState],
  template: `
    <mc-page-header [title]="i18n.t('users.title')" [subtitle]="i18n.t('users.subtitle')">
      <button type="button" mcButton="primary-solid" data-testid="add-user" (click)="openNew()"><mc-icon name="plus" [size]="16" /> {{ i18n.t('users.add') }}</button>
    </mc-page-header>
    <mc-tabs [tabs]="tabs()" [active]="tab()" (activeChange)="selectTab($event)" />
    <div class="toolbar"><mc-search-input [placeholder]="i18n.t('users.search')" (searched)="search.set($event); page.set(1); load()" /></div>
    @if (failed()) {
      <mc-error-state (retry)="load()" />
    } @else {
      <mc-users-table [users]="result()?.items ?? []" [loading]="loading()" [sort]="sort()" (sortChange)="sort.set($event ?? 'name'); load()" (action)="onAction($event.action, $event.user)" />
      @if (result(); as r) {
        <mc-pagination [page]="r.page" [pageSize]="r.pageSize" [total]="r.total" (pageChange)="page.set($event); load()" />
      }
    }
    <mc-drawer [open]="form() !== null" (openChange)="!$event && form.set(null)" [title]="form()?.id ? i18n.t('users.edit') : i18n.t('users.add')">
      @if (form(); as f) {
        <form class="mc-form" id="user-form" (ngSubmit)="save()">
          <label><span>{{ i18n.t('users.name') }}</span><input name="fullName" [(ngModel)]="f.fullName" required /></label>
          <label><span>{{ i18n.t('users.email') }}</span><input name="email" type="email" [(ngModel)]="f.email" [disabled]="!!f.id" required /></label>
          <label><span>{{ i18n.t('users.role') }}</span>
            <select name="role" [(ngModel)]="f.role">
              @for (role of roles; track role) {
                <option [value]="role">{{ i18n.t('role.' + role) }}</option>
              }
            </select>
          </label>
          <label><span>{{ i18n.t('users.locations') }}</span>
            <select name="locations" multiple [(ngModel)]="f.locationIds">
              @for (location of locations(); track location.id) {
                <option [value]="location.id">{{ location.name }}</option>
              }
            </select>
            <small>{{ i18n.t('users.locationsHint') }}</small>
          </label>
          @if (formError()) {
            <p class="field-error" role="alert">{{ formError() }}</p>
          }
        </form>
      }
      <div drawerActions>
        <button type="button" mcButton="secondary" (click)="form.set(null)">{{ i18n.t('common.cancel') }}</button>
        <button type="submit" form="user-form" mcButton="primary-solid" [disabled]="busy()">{{ i18n.t('common.save') }}</button>
      </div>
    </mc-drawer>
  `,
  styles: `
    .toolbar { display: flex; margin-block: var(--mc-space-4); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsersPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(UsersApi);
  private readonly locationsApi = inject(LocationsApi);
  private readonly toast = inject(ToastService);

  protected readonly roles = ROLES;
  protected readonly tab = signal('all');
  protected readonly search = signal('');
  protected readonly sort = signal('name');
  protected readonly page = signal(1);
  protected readonly counts = signal<Record<string, number>>({});
  protected readonly result = signal<Paged<UserListItem> | null>(null);
  protected readonly locations = signal<LocationCard[]>([]);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly form = signal<UserForm | null>(null);
  protected readonly formError = signal<string | null>(null);

  protected readonly tabs = computed<TabItem[]>(() => [
    { id: 'all', label: this.i18n.t('users.tabAll'), count: this.counts()['all'] },
    { id: 'Administrator', label: this.i18n.t('users.tabAdministrators'), count: this.counts()['Administrator'] },
    { id: 'ITManager', label: this.i18n.t('users.tabItManagers'), count: this.counts()['ITManager'] },
    { id: 'Technician', label: this.i18n.t('users.tabTechnicians'), count: this.counts()['Technician'] },
  ]);

  constructor() {
    void this.load();
    void this.loadCounts();
    void firstValueFrom(this.locationsApi.list({ pageSize: 200 })).then((r) => this.locations.set(r.items)).catch(() => undefined);
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);
    try {
      const role = this.tab() === 'all' ? null : this.tab();
      this.result.set(await firstValueFrom(this.api.list({ role, search: this.search(), sort: this.sort(), page: this.page(), pageSize: 20 })));
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  private async loadCounts(): Promise<void> {
    try {
      const [all, ...byRole] = await Promise.all([
        firstValueFrom(this.api.list({ pageSize: 1 })),
        ...ROLES.slice(0, 3).map((role) => firstValueFrom(this.api.list({ role, pageSize: 1 }))),
      ]);
      const counts: Record<string, number> = { all: all.total };
      ROLES.slice(0, 3).forEach((role, i) => (counts[role] = byRole[i]?.total ?? 0));
      this.counts.set(counts);
    } catch {
      this.counts.set({});
    }
  }

  protected selectTab(tab: string): void {
    this.tab.set(tab);
    this.page.set(1);
    void this.load();
  }

  protected openNew(): void {
    this.formError.set(null);
    this.form.set({ id: null, version: '', fullName: '', email: '', role: 'Technician', locationIds: [] });
  }

  async onAction(action: UserAction, user: UserListItem): Promise<void> {
    if (action === 'edit') {
      this.formError.set(null);
      this.form.set({ id: user.id, version: user.version, fullName: user.fullName, email: user.email, role: user.role, locationIds: [...user.locationIds] });
      return;
    }
    if (action === 'activate') {
      await firstValueFrom(this.api.activate(user.id));
    } else if (action === 'deactivate') {
      await firstValueFrom(this.api.deactivate(user.id));
    } else {
      await firstValueFrom(this.api.resendInvitation(user.id));
      this.toast.success(this.i18n.t('users.resent', { email: user.email }));
    }
    await Promise.all([this.load(), this.loadCounts()]);
  }

  async save(): Promise<void> {
    const f = this.form();
    if (!f) {
      return;
    }
    this.busy.set(true);
    this.formError.set(null);
    try {
      if (f.id) {
        await firstValueFrom(this.api.update(f.id, { fullName: f.fullName, role: f.role, locationIds: f.locationIds }, f.version));
      } else {
        await firstValueFrom(this.api.invite({ fullName: f.fullName, email: f.email, role: f.role, locationIds: f.locationIds }));
        this.toast.success(this.i18n.t('users.invited', { email: f.email }));
      }
      this.form.set(null);
      await Promise.all([this.load(), this.loadCounts()]);
    } catch (e) {
      this.formError.set(this.i18n.errorMessage(toProblem(e).code));
    } finally {
      this.busy.set(false);
    }
  }
}
