import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

import { DevicesApi, LocationsApi } from '../../core/api/api.services';
import { DeviceListItem, LocationCard } from '../../core/api/models';
import { toProblem } from '../../core/api/problem';
import { ArchiveApi, ArchiveContact, ArchiveFile, ArchiveNote, CustomerProfile, RemoteAccess, RemoteAccessSecret, saveFile } from '../../core/api/reports.api';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { ToastService } from '../../core/ui/toast.service';
import { LocationContext } from '../locations/location-context';
import { fileSize } from '../reports/reports-panel';
import { Button } from '../../shared/ui/button';
import { Card } from '../../shared/ui/card';
import { CellDef, Column, DataTable } from '../../shared/ui/data-table';
import { Drawer } from '../../shared/ui/drawer';
import { PageHeader } from '../../shared/ui/headers';
import { Icon } from '../../shared/ui/icon';
import { Skeleton } from '../../shared/ui/skeleton';
import { EmptyState, ErrorState } from '../../shared/ui/states';
import { StatusPill } from '../../shared/ui/status-pill';
import { TabItem, Tabs } from '../../shared/ui/tabs';

const TOOLS = ['AnyDesk', 'TeamViewer', 'RDP', 'SSH', 'VNC', 'Other'];
const MAX_UPLOAD = 10 * 1024 * 1024;

interface ProfileForm {
  industry: string;
  website: string;
  phone: string;
  address: string;
  accountManager: string;
}

interface ContactForm {
  id: string | null;
  name: string;
  email: string;
  phone: string;
  jobTitle: string;
  isPrimary: boolean;
}

interface RemoteForm {
  id: string | null;
  locationId: string;
  deviceId: string;
  tool: string;
  label: string;
  identifier: string;
  password: string;
}

const blank = (value: string): string | null => (value.trim() === '' ? null : value.trim());

/**
 * Customer Archive (07 section 5.8): Company Information, Contacts, Remote Access (platform roles only), Notes and
 * Files & Attachments. With `locationId` (location tab) it shows the remote access entries of that location and the files.
 */
@Component({
  selector: 'mc-archive-view',
  imports: [FormsModule, NgTemplateOutlet, Tabs, Card, DataTable, CellDef, Drawer, Button, Icon, Skeleton, EmptyState, ErrorState, StatusPill],
  template: `
    <mc-tabs [tabs]="tabs()" [(active)]="tab" />

    @if (notEntitled()) {
      <mc-empty-state icon="archive" [title]="i18n.t('archive.notEntitled')" [message]="i18n.t('settings.notInPlan')" data-testid="archive-not-entitled" />
    } @else if (failed()) {
      <mc-error-state (retry)="load()" />
    } @else if (tab() === 'company') {
      <div class="grid">
        <mc-card [title]="i18n.t('archive.companyDetails')" data-testid="company-details">
          @if (canManage()) {
            <button cardActions type="button" mcButton="primary-outline" size="sm" (click)="editProfile()" data-testid="edit-profile"><mc-icon name="edit" [size]="14" /> {{ i18n.t('common.edit') }}</button>
          }
          @if (profile(); as p) {
            <dl class="facts">
              <dt>{{ i18n.t('archive.companyName') }}</dt><dd>{{ p.companyName }}</dd>
              <dt>{{ i18n.t('archive.industry') }}</dt><dd>{{ p.industry || '—' }}</dd>
              <dt>{{ i18n.t('archive.website') }}</dt><dd>
                @if (p.website) {
                  <a [href]="p.website" target="_blank" rel="noopener noreferrer">{{ p.website }}</a>
                } @else { — }
              </dd>
              <dt>{{ i18n.t('archive.phone') }}</dt><dd>{{ p.phone || '—' }}</dd>
              <dt>{{ i18n.t('locations.address') }}</dt><dd>{{ p.address || '—' }}</dd>
              <dt>{{ i18n.t('archive.customerSince') }}</dt><dd>{{ day(p.customerSince) }}</dd>
              <dt>{{ i18n.t('archive.accountManager') }}</dt><dd>{{ p.accountManager || '—' }}</dd>
              <dt>{{ i18n.t('common.status') }}</dt><dd><mc-status-pill [status]="p.status.toLowerCase()" /></dd>
            </dl>
          } @else {
            <mc-skeleton [height]="240" />
          }
        </mc-card>
        <mc-card [title]="i18n.t('archive.recentFiles')" [flush]="true" data-testid="recent-files">
          <ng-container *ngTemplateOutlet="fileList; context: { $implicit: recentFiles() }" />
        </mc-card>
      </div>
    } @else if (tab() === 'contacts') {
      <mc-card [title]="i18n.t('archive.contacts')" [flush]="true" class="block" data-testid="contacts">
        @if (canManage()) {
          <button cardActions type="button" mcButton="primary-outline" size="sm" (click)="openContact(null)" data-testid="add-contact"><mc-icon name="plus" [size]="14" /> {{ i18n.t('archive.addContact') }}</button>
        }
        <mc-data-table [columns]="contactColumns()" [rows]="contacts()" [emptyText]="i18n.t('archive.noContacts')">
          <ng-template mcCell="name" let-row><strong>{{ c(row).name }}</strong>@if (c(row).isPrimary) { <mc-status-pill status="info" [label]="i18n.t('archive.primary')" /> }</ng-template>
          <ng-template mcCell="email" let-row>
            @if (c(row).email) { <a [href]="'mailto:' + c(row).email">{{ c(row).email }}</a> } @else { — }
          </ng-template>
          <ng-template mcCell="phone" let-row>{{ c(row).phone || '—' }}</ng-template>
          <ng-template mcCell="jobTitle" let-row>{{ c(row).jobTitle || '—' }}</ng-template>
          <ng-template mcCell="actions" let-row>
            @if (canManage()) {
              <button type="button" mcButton="ghost" size="sm" (click)="openContact(c(row))">{{ i18n.t('common.edit') }}</button>
              <button type="button" mcButton="ghost" size="sm" (click)="deleteContact(c(row))">{{ i18n.t('common.delete') }}</button>
            }
          </ng-template>
        </mc-data-table>
      </mc-card>
    } @else if (tab() === 'remote') {
      <mc-card [title]="i18n.t('archive.remoteAccess')" [flush]="true" class="block" data-testid="remote-access">
        <button cardActions type="button" mcButton="primary-outline" size="sm" (click)="openRemote(null)" data-testid="add-remote"><mc-icon name="plus" [size]="14" /> {{ i18n.t('archive.addRemote') }}</button>
        <p class="hint">{{ i18n.t('archive.remoteHint') }}</p>
        <mc-data-table [columns]="remoteColumns()" [rows]="remoteRows()" [emptyText]="i18n.t('archive.noRemote')">
          <ng-template mcCell="label" let-row><strong>{{ ra(row).label }}</strong></ng-template>
          <ng-template mcCell="where" let-row>{{ where(ra(row)) }}</ng-template>
          <ng-template mcCell="identifier" let-row>
            <code>{{ revealed()[ra(row).id]?.identifier ?? ra(row).identifier }}</code>
          </ng-template>
          <ng-template mcCell="password" let-row>
            @if (ra(row).hasPassword) {
              <code>{{ revealed()[ra(row).id]?.password ?? '••••••••' }}</code>
            } @else { — }
          </ng-template>
          <ng-template mcCell="actions" let-row>
            @if (revealed()[ra(row).id]) {
              <button type="button" mcButton="ghost" size="sm" (click)="hide(ra(row))">{{ i18n.t('archive.hide') }}</button>
            } @else {
              <button type="button" mcButton="ghost" size="sm" (click)="reveal(ra(row))" [attr.data-testid]="'reveal-' + ra(row).id"><mc-icon name="eye" [size]="14" /> {{ i18n.t('archive.reveal') }}</button>
            }
            <button type="button" mcButton="ghost" size="sm" (click)="openRemote(ra(row))">{{ i18n.t('common.edit') }}</button>
            <button type="button" mcButton="ghost" size="sm" (click)="deleteRemote(ra(row))">{{ i18n.t('common.delete') }}</button>
          </ng-template>
        </mc-data-table>
      </mc-card>
    } @else if (tab() === 'notes') {
      <div class="notes block" data-testid="notes">
        @if (canManage()) {
          <mc-card [title]="i18n.t('archive.addNote')">
            <form class="mc-form" (ngSubmit)="addNote()">
              <textarea name="note" rows="3" maxlength="4000" [(ngModel)]="noteBody" [attr.aria-label]="i18n.t('archive.addNote')" data-testid="note-body"></textarea>
              <div class="note-actions">
                @if (seesInternal()) {
                  <label class="inline"><input type="checkbox" name="internal" [(ngModel)]="noteInternal" data-testid="note-internal" /> {{ i18n.t('archive.internalOnly') }}</label>
                }
                <button type="submit" mcButton="primary-solid" [disabled]="busy() || noteBody.trim() === ''" data-testid="save-note">{{ i18n.t('common.save') }}</button>
              </div>
            </form>
          </mc-card>
        }
        @for (n of notes(); track n.id) {
          <article class="note" [class.internal]="n.isInternal" [attr.data-testid]="'note-' + n.id">
            <header>
              <strong>{{ n.authorName }}</strong>
              <small>{{ date(n.createdAt) }}</small>
              @if (n.isInternal) { <mc-status-pill status="warning" [label]="i18n.t('archive.internal')" /> }
              @if (canManage()) {
                <button type="button" mcButton="ghost" size="sm" (click)="deleteNote(n)" [attr.aria-label]="i18n.t('common.delete')"><mc-icon name="trash" [size]="14" /></button>
              }
            </header>
            <p>{{ n.body }}</p>
          </article>
        } @empty {
          <mc-empty-state icon="archive" [title]="i18n.t('archive.noNotes')" />
        }
      </div>
    } @else if (tab() === 'files') {
      <mc-card [title]="i18n.t('archive.files')" [flush]="true" class="block" data-testid="files">
        @if (canManage()) {
          <button cardActions type="button" mcButton="primary-outline" size="sm" [disabled]="busy()" (click)="picker.click()" data-testid="upload-button">
            <mc-icon name="upload" [size]="14" /> {{ i18n.t('archive.upload') }}
          </button>
        }
        @if (canManage() && seesInternal()) {
          <label class="inline internal-toggle"><input type="checkbox" [(ngModel)]="uploadInternal" /> {{ i18n.t('archive.uploadInternal') }}</label>
        }
        <ng-container *ngTemplateOutlet="fileList; context: { $implicit: files() }" />
      </mc-card>
    }

    <input #picker type="file" hidden (change)="upload($event)" [accept]="accept" [attr.aria-label]="i18n.t('archive.upload')" data-testid="upload-input" />

    <ng-template #fileList let-list>
      @if (loaded()) {
        <ul class="files">
          @for (f of asFiles(list); track f.id) {
            <li [attr.data-testid]="'file-' + f.id">
              <mc-icon name="file" [size]="18" />
              <div class="what"><strong>{{ f.displayName }}</strong><small>{{ date(f.uploadedAt) }} · {{ size(f.sizeBytes) }}</small></div>
              @if (f.isInternal) { <mc-status-pill status="warning" [label]="i18n.t('archive.internal')" /> }
              <button type="button" mcButton="ghost" size="sm" (click)="download(f)" [attr.aria-label]="i18n.t('reports.download') + ' ' + f.displayName"><mc-icon name="download" [size]="16" /></button>
              @if (canManage() && tab() === 'files') {
                <button type="button" mcButton="ghost" size="sm" (click)="deleteFile(f)" [attr.aria-label]="i18n.t('common.delete') + ' ' + f.displayName"><mc-icon name="trash" [size]="16" /></button>
              }
            </li>
          } @empty {
            <li class="none"><mc-empty-state icon="file" [title]="i18n.t('archive.noFiles')" /></li>
          }
        </ul>
      } @else {
        <mc-skeleton [height]="200" />
      }
    </ng-template>

    <mc-drawer [open]="profileForm() !== null" (openChange)="!$event && profileForm.set(null)" [title]="i18n.t('archive.editProfile')">
      @if (profileForm(); as f) {
        <form class="mc-form" id="profile-form" (ngSubmit)="saveProfile()">
          <label><span>{{ i18n.t('archive.industry') }}</span><input name="industry" maxlength="100" [(ngModel)]="f.industry" /></label>
          <label><span>{{ i18n.t('archive.website') }}</span><input name="website" type="url" maxlength="200" placeholder="https://" [(ngModel)]="f.website" /></label>
          <label><span>{{ i18n.t('archive.phone') }}</span><input name="phone" maxlength="50" [(ngModel)]="f.phone" /></label>
          <label><span>{{ i18n.t('locations.address') }}</span><input name="address" maxlength="300" [(ngModel)]="f.address" /></label>
          <label><span>{{ i18n.t('archive.accountManager') }}</span><input name="accountManager" maxlength="200" [(ngModel)]="f.accountManager" /></label>
          @if (formError()) { <p class="field-error" role="alert">{{ formError() }}</p> }
        </form>
      }
      <div drawerActions>
        <button type="button" mcButton="secondary" (click)="profileForm.set(null)">{{ i18n.t('common.cancel') }}</button>
        <button type="submit" form="profile-form" mcButton="primary-solid" [disabled]="busy()" data-testid="save-profile">{{ i18n.t('common.save') }}</button>
      </div>
    </mc-drawer>

    <mc-drawer [open]="contactForm() !== null" (openChange)="!$event && contactForm.set(null)" [title]="contactForm()?.id ? i18n.t('archive.editContact') : i18n.t('archive.addContact')">
      @if (contactForm(); as f) {
        <form class="mc-form" id="contact-form" (ngSubmit)="saveContact()">
          <label><span>{{ i18n.t('users.name') }}</span><input name="name" required maxlength="200" [(ngModel)]="f.name" data-testid="contact-name" /></label>
          <label><span>{{ i18n.t('users.email') }}</span><input name="email" type="email" maxlength="256" [(ngModel)]="f.email" /></label>
          <label><span>{{ i18n.t('archive.phone') }}</span><input name="phone" maxlength="50" [(ngModel)]="f.phone" /></label>
          <label><span>{{ i18n.t('archive.jobTitle') }}</span><input name="jobTitle" maxlength="100" [(ngModel)]="f.jobTitle" /></label>
          <label class="inline"><input type="checkbox" name="isPrimary" [(ngModel)]="f.isPrimary" /> <span>{{ i18n.t('archive.primary') }}</span></label>
          @if (formError()) { <p class="field-error" role="alert">{{ formError() }}</p> }
        </form>
      }
      <div drawerActions>
        <button type="button" mcButton="secondary" (click)="contactForm.set(null)">{{ i18n.t('common.cancel') }}</button>
        <button type="submit" form="contact-form" mcButton="primary-solid" [disabled]="busy()" data-testid="save-contact">{{ i18n.t('common.save') }}</button>
      </div>
    </mc-drawer>

    <mc-drawer [open]="remoteForm() !== null" (openChange)="!$event && remoteForm.set(null)" [title]="remoteForm()?.id ? i18n.t('archive.editRemote') : i18n.t('archive.addRemote')">
      @if (remoteForm(); as f) {
        <form class="mc-form" id="remote-form" (ngSubmit)="saveRemote()" autocomplete="off">
          <label><span>{{ i18n.t('archive.tool') }}</span>
            <select name="tool" [(ngModel)]="f.tool">
              @for (t of tools; track t) { <option [value]="t">{{ t }}</option> }
            </select>
          </label>
          <label><span>{{ i18n.t('archive.label') }}</span><input name="label" required maxlength="200" [(ngModel)]="f.label" data-testid="remote-label" /></label>
          <label><span>{{ i18n.t('dashboard.location') }}</span>
            <select name="locationId" [(ngModel)]="f.locationId" [disabled]="!!locationId()">
              <option value="">—</option>
              @for (l of locations(); track l.id) { <option [value]="l.id">{{ l.name }}</option> }
            </select>
          </label>
          <label><span>{{ i18n.t('alerts.device') }}</span>
            <select name="deviceId" [(ngModel)]="f.deviceId">
              <option value="">—</option>
              @for (d of devicesFor(f.locationId); track d.id) { <option [value]="d.id">{{ d.name }}</option> }
            </select>
          </label>
          <label><span>{{ i18n.t('archive.identifier') }}</span><input name="identifier" required maxlength="500" [(ngModel)]="f.identifier" data-testid="remote-identifier" /></label>
          <label><span>{{ i18n.t('login.password') }}</span>
            <input name="password" type="password" maxlength="500" autocomplete="new-password" [(ngModel)]="f.password" [placeholder]="f.id ? i18n.t('archive.keepPassword') : ''" data-testid="remote-password" />
          </label>
          @if (formError()) { <p class="field-error" role="alert">{{ formError() }}</p> }
        </form>
      }
      <div drawerActions>
        <button type="button" mcButton="secondary" (click)="remoteForm.set(null)">{{ i18n.t('common.cancel') }}</button>
        <button type="submit" form="remote-form" mcButton="primary-solid" [disabled]="busy()" data-testid="save-remote">{{ i18n.t('common.save') }}</button>
      </div>
    </mc-drawer>
  `,
  styles: `
    :host { display: block; }
    .block, .grid { margin-block-start: var(--mc-space-4); }
    .grid { display: grid; grid-template-columns: minmax(0, 1.2fr) minmax(0, 1fr); gap: var(--mc-space-4); align-items: start; }
    @media (max-width: 1000px) { .grid { grid-template-columns: 1fr; } }
    .facts { display: grid; grid-template-columns: max-content 1fr; gap: var(--mc-space-3) var(--mc-space-5); margin: 0; }
    .facts dt { color: var(--mc-text-muted); }
    .facts dd { margin: 0; overflow-wrap: anywhere; }
    .hint { color: var(--mc-text-muted); margin: var(--mc-space-3) var(--mc-space-4) 0; font-size: var(--mc-fs-sm); }
    .files { list-style: none; margin: 0; padding: 0; }
    .files li { display: flex; align-items: center; gap: var(--mc-space-3); padding: var(--mc-space-3) var(--mc-space-4); border-block-end: 1px solid var(--mc-border); }
    .files li:last-child { border-block-end: 0; }
    .files li.none { display: block; }
    .what { flex: 1; min-inline-size: 0; display: flex; flex-direction: column; gap: 2px; }
    .what strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .what small { color: var(--mc-text-muted); }
    .notes { display: flex; flex-direction: column; gap: var(--mc-space-3); }
    .note { background: var(--mc-bg-card); border: 1px solid var(--mc-border); border-radius: var(--mc-radius-md); padding: var(--mc-space-3) var(--mc-space-4); }
    .note.internal { border-inline-start: 3px solid var(--mc-warning); }
    .note header { display: flex; align-items: center; gap: var(--mc-space-2); }
    .note header small { color: var(--mc-text-muted); }
    .note header button { margin-inline-start: auto; }
    .note p { margin: var(--mc-space-2) 0 0; white-space: pre-wrap; overflow-wrap: anywhere; }
    .note-actions { display: flex; align-items: center; justify-content: space-between; gap: var(--mc-space-3); }
    textarea { background: var(--mc-bg-input); border: 1px solid var(--mc-border); border-radius: var(--mc-radius-md); color: var(--mc-text); font: inherit; padding: var(--mc-space-2); resize: vertical; }
    .inline { display: flex; flex-direction: row; align-items: center; gap: var(--mc-space-2); }
    .internal-toggle { padding: var(--mc-space-3) var(--mc-space-4) 0; }
    code { font-family: var(--mc-font-mono, monospace); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ArchiveView {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(ArchiveApi);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly locationsApi = inject(LocationsApi);
  private readonly devicesApi = inject(DevicesApi);

  /** Location tab: only that location's remote access and the files. */
  readonly locationId = input<string | null>(null);

  protected readonly tools = TOOLS;
  protected readonly accept = '.pdf,.csv,.txt,.png,.jpg,.jpeg,.docx,.xlsx,.zip';
  protected readonly tab = signal('company');
  protected readonly profile = signal<CustomerProfile | null>(null);
  protected readonly contacts = signal<ArchiveContact[]>([]);
  protected readonly notes = signal<ArchiveNote[]>([]);
  protected readonly files = signal<ArchiveFile[]>([]);
  protected readonly remote = signal<RemoteAccess[]>([]);
  protected readonly revealed = signal<Record<string, RemoteAccessSecret>>({});
  protected readonly locations = signal<LocationCard[]>([]);
  protected readonly devices = signal<DeviceListItem[]>([]);
  protected readonly loaded = signal(false);
  protected readonly failed = signal(false);
  protected readonly notEntitled = signal(false);
  protected readonly busy = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly profileForm = signal<ProfileForm | null>(null);
  protected readonly contactForm = signal<ContactForm | null>(null);
  protected readonly remoteForm = signal<RemoteForm | null>(null);
  protected noteBody = '';
  protected noteInternal = false;
  protected uploadInternal = false;

  protected readonly canManage = computed(() => this.auth.hasPermission('archive.manage'));
  protected readonly seesInternal = computed(() => this.auth.hasPermission('archive.internal'));
  protected readonly recentFiles = computed(() => this.files().slice(0, 5));
  protected readonly remoteRows = computed(() => {
    const location = this.locationId();
    return location ? this.remote().filter((r) => r.locationId === location) : this.remote();
  });

  protected readonly tabs = computed<TabItem[]>(() => {
    const location = !!this.locationId();
    const tabs: TabItem[] = [];
    if (!location) {
      tabs.push({ id: 'company', label: this.i18n.t('archive.company') }, { id: 'contacts', label: this.i18n.t('archive.contacts') });
    }
    if (this.seesInternal()) {
      tabs.push({ id: 'remote', label: this.i18n.t('archive.remoteAccess') });
    }
    if (!location) {
      tabs.push({ id: 'notes', label: this.i18n.t('archive.notes') });
    }
    tabs.push({ id: 'files', label: this.i18n.t('archive.files') });
    return tabs;
  });

  protected readonly contactColumns = computed<Column[]>(() => [
    { key: 'name', label: this.i18n.t('users.name') },
    { key: 'email', label: this.i18n.t('users.email') },
    { key: 'phone', label: this.i18n.t('archive.phone') },
    { key: 'jobTitle', label: this.i18n.t('archive.jobTitle') },
    { key: 'actions', label: '', width: '150px' },
  ]);

  protected readonly remoteColumns = computed<Column[]>(() => [
    { key: 'label', label: this.i18n.t('archive.label') },
    { key: 'tool', label: this.i18n.t('archive.tool') },
    { key: 'where', label: this.i18n.t('dashboard.location') },
    { key: 'identifier', label: this.i18n.t('archive.identifier') },
    { key: 'password', label: this.i18n.t('login.password') },
    { key: 'actions', label: '', width: '240px' },
  ]);

  constructor() {
    void this.load();
  }

  async load(): Promise<void> {
    this.failed.set(false);
    if (this.locationId() && this.tab() === 'company') {
      this.tab.set(this.seesInternal() ? 'remote' : 'files');
    }
    try {
      // Files first: a plan without the archive answers FEATURE_NOT_ENTITLED here, without an error toast.
      await firstValueFrom(this.api.files());
    } catch (e) {
      if (toProblem(e).code === 'FEATURE_NOT_ENTITLED') {
        this.notEntitled.set(true);
      } else {
        this.failed.set(true);
      }
      return;
    }
    try {
      const location = !!this.locationId();
      const [profile, contacts, notes, files, remote] = await Promise.all([
        location ? Promise.resolve(null) : firstValueFrom(this.api.profile()),
        location ? Promise.resolve([]) : firstValueFrom(this.api.contacts()),
        location ? Promise.resolve([]) : firstValueFrom(this.api.notes()),
        firstValueFrom(this.api.files()),
        this.seesInternal() ? firstValueFrom(this.api.remoteAccess()) : Promise.resolve([]),
      ]);
      this.profile.set(profile);
      this.contacts.set(contacts);
      this.notes.set(notes);
      this.files.set(files);
      this.remote.set(remote);
      this.loaded.set(true);
      if (this.seesInternal()) {
        void this.loadPlaces();
      }
    } catch {
      this.failed.set(true);
    }
  }

  private async loadPlaces(): Promise<void> {
    try {
      const [locations, devices] = await Promise.all([firstValueFrom(this.locationsApi.list({ pageSize: 200 })), firstValueFrom(this.devicesApi.list({ pageSize: 200, sort: 'name' }))]);
      this.locations.set(locations.items);
      this.devices.set(devices.items);
    } catch {
      // The names are optional (the list shows ids otherwise).
    }
  }

  protected c(row: unknown): ArchiveContact {
    return row as ArchiveContact;
  }

  protected ra(row: unknown): RemoteAccess {
    return row as RemoteAccess;
  }

  protected asFiles(list: unknown): ArchiveFile[] {
    return list as ArchiveFile[];
  }

  protected devicesFor(locationId: string): DeviceListItem[] {
    return locationId ? this.devices().filter((d) => d.locationId === locationId) : this.devices();
  }

  protected where(entry: RemoteAccess): string {
    const location = this.locations().find((l) => l.id === entry.locationId)?.name;
    const device = this.devices().find((d) => d.id === entry.deviceId)?.name;
    return [location, device].filter((p) => !!p).join(' · ') || '—';
  }

  protected date(at: string): string {
    return new Date(at).toLocaleString(this.locale(), { dateStyle: 'medium', timeStyle: 'short' });
  }

  protected day(date: string): string {
    return new Date(date).toLocaleDateString(this.locale(), { dateStyle: 'medium' });
  }

  protected size(bytes: number): string {
    return fileSize(bytes);
  }

  private locale(): string {
    return this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB';
  }

  private fail(error: unknown): void {
    this.formError.set(this.i18n.errorMessage(toProblem(error).code));
  }

  protected editProfile(): void {
    const p = this.profile();
    this.formError.set(null);
    this.profileForm.set({ industry: p?.industry ?? '', website: p?.website ?? '', phone: p?.phone ?? '', address: p?.address ?? '', accountManager: p?.accountManager ?? '' });
  }

  protected async saveProfile(): Promise<void> {
    const f = this.profileForm();
    if (!f) {
      return;
    }
    this.busy.set(true);
    this.formError.set(null);
    try {
      await firstValueFrom(
        this.api.updateProfile({ industry: blank(f.industry), website: blank(f.website), phone: blank(f.phone), address: blank(f.address), accountManager: blank(f.accountManager) }),
      );
      this.profileForm.set(null);
      this.profile.set(await firstValueFrom(this.api.profile()));
      this.toast.success(this.i18n.t('settings.saved'));
    } catch (e) {
      this.fail(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected openContact(contact: ArchiveContact | null): void {
    this.formError.set(null);
    this.contactForm.set(
      contact
        ? { id: contact.id, name: contact.name, email: contact.email ?? '', phone: contact.phone ?? '', jobTitle: contact.jobTitle ?? '', isPrimary: contact.isPrimary }
        : { id: null, name: '', email: '', phone: '', jobTitle: '', isPrimary: this.contacts().length === 0 },
    );
  }

  protected async saveContact(): Promise<void> {
    const f = this.contactForm();
    if (!f) {
      return;
    }
    this.busy.set(true);
    this.formError.set(null);
    try {
      await firstValueFrom(this.api.saveContact(f.id, { name: f.name.trim(), email: blank(f.email), phone: blank(f.phone), jobTitle: blank(f.jobTitle), isPrimary: f.isPrimary }));
      this.contactForm.set(null);
      this.contacts.set(await firstValueFrom(this.api.contacts()));
    } catch (e) {
      this.fail(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected async deleteContact(contact: ArchiveContact): Promise<void> {
    try {
      await firstValueFrom(this.api.deleteContact(contact.id));
      this.contacts.update((list) => list.filter((x) => x.id !== contact.id));
    } catch {
      // The error toast explains why.
    }
  }

  protected async addNote(): Promise<void> {
    const body = this.noteBody.trim();
    if (!body) {
      return;
    }
    this.busy.set(true);
    try {
      const note = await firstValueFrom(this.api.addNote(body, this.seesInternal() && this.noteInternal));
      this.notes.update((list) => [note, ...list]);
      this.noteBody = '';
      this.noteInternal = false;
    } catch {
      // The error toast explains why.
    } finally {
      this.busy.set(false);
    }
  }

  protected async deleteNote(note: ArchiveNote): Promise<void> {
    try {
      await firstValueFrom(this.api.deleteNote(note.id));
      this.notes.update((list) => list.filter((x) => x.id !== note.id));
    } catch {
      // The error toast explains why.
    }
  }

  protected async upload(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }
    if (file.size > MAX_UPLOAD) {
      this.toast.error(this.i18n.errorMessage('UPLOAD_TOO_LARGE'));
      return;
    }
    this.busy.set(true);
    try {
      const stored = await firstValueFrom(this.api.upload(file, null, this.seesInternal() && this.uploadInternal));
      this.files.update((list) => [stored, ...list]);
      this.toast.success(this.i18n.t('archive.uploaded'));
    } catch {
      // The error toast explains why.
    } finally {
      this.busy.set(false);
    }
  }

  protected async download(file: ArchiveFile): Promise<void> {
    try {
      saveFile(await firstValueFrom(this.api.download(file.id)));
    } catch {
      // The error toast explains why.
    }
  }

  protected async deleteFile(file: ArchiveFile): Promise<void> {
    try {
      await firstValueFrom(this.api.deleteFile(file.id));
      this.files.update((list) => list.filter((x) => x.id !== file.id));
    } catch {
      // The error toast explains why.
    }
  }

  protected openRemote(entry: RemoteAccess | null): void {
    this.formError.set(null);
    this.remoteForm.set(
      entry
        ? { id: entry.id, locationId: entry.locationId ?? '', deviceId: entry.deviceId ?? '', tool: entry.tool, label: entry.label, identifier: '', password: '' }
        : { id: null, locationId: this.locationId() ?? '', deviceId: '', tool: TOOLS[0], label: '', identifier: '', password: '' },
    );
    if (entry) {
      // The identifier is only shown masked; editing needs the real value, so it is revealed (and audited).
      void this.reveal(entry).then(() => {
        const secret = this.revealed()[entry.id];
        this.remoteForm.update((f) => (f && f.id === entry.id ? { ...f, identifier: secret?.identifier ?? '' } : f));
      });
    }
  }

  protected async saveRemote(): Promise<void> {
    const f = this.remoteForm();
    if (!f) {
      return;
    }
    this.busy.set(true);
    this.formError.set(null);
    try {
      await firstValueFrom(
        this.api.saveRemoteAccess(f.id, {
          locationId: f.locationId || null,
          deviceId: f.deviceId || null,
          tool: f.tool,
          label: f.label.trim(),
          identifier: f.identifier.trim(),
          // Empty keeps the stored password on edit.
          password: f.password || null,
        }),
      );
      this.remoteForm.set(null);
      this.revealed.set({});
      this.remote.set(await firstValueFrom(this.api.remoteAccess()));
    } catch (e) {
      this.fail(e);
    } finally {
      this.busy.set(false);
    }
  }

  protected async reveal(entry: RemoteAccess): Promise<void> {
    try {
      const secret = await firstValueFrom(this.api.reveal(entry.id));
      this.revealed.update((map) => ({ ...map, [entry.id]: secret }));
    } catch {
      // The error toast explains why.
    }
  }

  protected hide(entry: RemoteAccess): void {
    this.revealed.update((map) => {
      const next = { ...map };
      delete next[entry.id];
      return next;
    });
  }

  protected async deleteRemote(entry: RemoteAccess): Promise<void> {
    try {
      await firstValueFrom(this.api.deleteRemoteAccess(entry.id));
      this.remote.update((list) => list.filter((x) => x.id !== entry.id));
    } catch {
      // The error toast explains why.
    }
  }
}

/** Customer Archive page (`.../archive`). */
@Component({
  selector: 'mc-archive-page',
  imports: [PageHeader, ArchiveView],
  template: `
    <mc-page-header [title]="i18n.t('nav.archive')" [subtitle]="i18n.t('archive.subtitle')" />
    <mc-archive-view />
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ArchivePage {
  protected readonly i18n = inject(I18nService);
}

/** Location > Archive. */
@Component({
  selector: 'mc-location-archive-page',
  imports: [ArchiveView],
  template: `
    @if (context.id()) {
      <mc-archive-view [locationId]="context.id()" />
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LocationArchivePage {
  protected readonly context = inject(LocationContext);
}
