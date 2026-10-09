import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { LocationsApi } from '../../core/api/api.services';
import { LocationCard } from '../../core/api/models';
import { AlertSettings, ConfigDocument, ConfigurationApi, GeneralSettings, Recipient, SettingsApi } from '../../core/api/monitoring.api';
import { Integrations, IntegrationsApi, WebhookTest } from '../../core/api/reports.api';
import { ThresholdsForm, editable, normalised } from '../configuration/thresholds-form';
import { toProblem } from '../../core/api/problem';
import { I18nService } from '../../core/i18n/i18n.service';
import { ToastService } from '../../core/ui/toast.service';
import { Button } from '../../shared/ui/button';
import { Card } from '../../shared/ui/card';
import { CellDef, Column, DataTable } from '../../shared/ui/data-table';
import { Drawer } from '../../shared/ui/drawer';
import { PageHeader } from '../../shared/ui/headers';
import { Icon } from '../../shared/ui/icon';
import { Skeleton } from '../../shared/ui/skeleton';
import { ErrorState } from '../../shared/ui/states';
import { StatusPill } from '../../shared/ui/status-pill';
import { TabItem, Tabs } from '../../shared/ui/tabs';

const TIME_ZONES = ['Africa/Cairo', 'Asia/Dubai', 'Asia/Riyadh', 'Asia/Kuwait', 'Asia/Qatar', 'Asia/Amman', 'Europe/London', 'Europe/Berlin', 'UTC'];
const EVENTS = ['All', 'WarningsAndCritical', 'CriticalOnly'];

interface Channels {
  emailEnabled: boolean;
  inAppEnabled: boolean;
  webhookEnabled: boolean;
  webhookUrl: string;
}

interface RecipientForm {
  id: string | null;
  name: string;
  email: string;
  events: string;
  locationId: string;
  isActive: boolean;
}

/** Settings (07 section 5.8): General, Alert Settings, Monitoring (default thresholds), Locations (links) and Integrations (webhook). */
@Component({
  selector: 'mc-settings-page',
  imports: [FormsModule, RouterLink, PageHeader, Tabs, Card, DataTable, CellDef, Drawer, Button, Icon, Skeleton, ErrorState, StatusPill, ThresholdsForm],
  template: `
    <mc-page-header [title]="i18n.t('nav.settings')" [subtitle]="i18n.t('settings.subtitle')" />
    <mc-tabs [tabs]="tabs()" [(active)]="tab" />

    @if (failed()) {
      <mc-error-state (retry)="load()" />
    } @else if (tab() === 'general') {
      @if (general(); as g) {
        <mc-card [title]="i18n.t('settings.general')" class="block" data-testid="general-settings">
          <form class="mc-form narrow" (ngSubmit)="saveGeneral()">
            <label><span>{{ i18n.t('locations.timeZone') }}</span>
              <select name="timeZone" [(ngModel)]="g.timeZone">
                @for (zone of zones(g.timeZone); track zone) {
                  <option [value]="zone">{{ zone }}</option>
                }
              </select>
            </label>
            <label><span>{{ i18n.t('settings.defaultLanguage') }}</span>
              <select name="defaultLanguage" [(ngModel)]="g.defaultLanguage">
                <option value="en">{{ i18n.t('language.en') }}</option>
                <option value="ar">{{ i18n.t('language.ar') }}</option>
              </select>
            </label>
            <label><span>{{ i18n.t('settings.offlineSeverity') }}</span>
              <select name="offlineAlertSeverity" [(ngModel)]="g.offlineAlertSeverity" data-testid="offline-severity">
                @for (s of severities; track s) {
                  <option [value]="s">{{ i18n.t('severity.' + s.toLowerCase()) }}</option>
                }
              </select>
            </label>
            <label><span>{{ i18n.t('settings.offlineDelay') }}</span>
              <input name="offlineAlertDelayMinutes" type="number" min="1" max="60" [(ngModel)]="g.offlineAlertDelayMinutes" required data-testid="offline-delay" />
              <small>{{ i18n.t('settings.offlineDelayHint') }}</small>
            </label>
            <div class="actions"><button type="submit" mcButton="primary-solid" [disabled]="busy()" data-testid="save-general">{{ i18n.t('common.save') }}</button></div>
          </form>
        </mc-card>
      } @else {
        <mc-skeleton [height]="280" />
      }
    } @else if (tab() === 'alerts') {
      @if (alerts(); as a) {
        <div class="grid">
          <mc-card [title]="i18n.t('settings.alertChannels')" data-testid="alert-channels">
            <ul class="channels">
              <li>
                <div><strong>{{ i18n.t('settings.email') }}</strong><small>{{ a.emailEntitled ? i18n.t('settings.emailHint') : i18n.t('settings.notInPlan') }}</small></div>
                <input type="checkbox" role="switch" [checked]="channels.emailEnabled" (change)="channels.emailEnabled = $any($event.target).checked" [disabled]="!a.emailEntitled" [attr.aria-label]="i18n.t('settings.email')" data-testid="switch-email" />
              </li>
              <li>
                <div><strong>{{ i18n.t('settings.sms') }}</strong><small>{{ i18n.t('settings.notAvailable') }}</small></div>
                <input type="checkbox" role="switch" [checked]="false" disabled [attr.aria-label]="i18n.t('settings.sms')" data-testid="switch-sms" />
              </li>
              <li>
                <div><strong>{{ i18n.t('settings.inApp') }}</strong><small>{{ i18n.t('settings.inAppHint') }}</small></div>
                <input type="checkbox" role="switch" [checked]="channels.inAppEnabled" (change)="channels.inAppEnabled = $any($event.target).checked" [attr.aria-label]="i18n.t('settings.inApp')" data-testid="switch-inapp" />
              </li>
              <li>
                <div><strong>{{ i18n.t('settings.webhook') }}</strong><small>{{ a.webhookEntitled ? i18n.t('settings.webhookHint') : i18n.t('settings.notInPlan') }}</small></div>
                <input type="checkbox" role="switch" [checked]="channels.webhookEnabled" (change)="channels.webhookEnabled = $any($event.target).checked" [disabled]="!a.webhookEntitled" [attr.aria-label]="i18n.t('settings.webhook')" data-testid="switch-webhook" />
              </li>
            </ul>
            @if (channels.webhookEnabled) {
              <label class="mc-form"><span>{{ i18n.t('settings.webhookUrl') }}</span><input type="url" [(ngModel)]="channels.webhookUrl" placeholder="https://" /></label>
            }
            <div class="actions"><button type="button" mcButton="primary-solid" [disabled]="busy()" (click)="saveChannels()" data-testid="save-channels">{{ i18n.t('common.save') }}</button></div>
          </mc-card>

          <mc-card [title]="i18n.t('settings.recipients')" [flush]="true" data-testid="recipients">
            <button cardActions type="button" mcButton="primary-outline" size="sm" (click)="openRecipient(null)" data-testid="add-recipient"><mc-icon name="plus" [size]="14" /> {{ i18n.t('settings.addRecipient') }}</button>
            <mc-data-table [columns]="recipientColumns()" [rows]="recipients()" [emptyText]="i18n.t('settings.noRecipients')">
              <ng-template mcCell="name" let-row><strong>{{ r(row).name }}</strong></ng-template>
              <ng-template mcCell="events" let-row>{{ i18n.t('settings.events.' + r(row).events) }}@if (r(row).locationName) { · {{ r(row).locationName }} }</ng-template>
              <ng-template mcCell="isActive" let-row><mc-status-pill [status]="r(row).isActive ? 'active' : 'inactive'" /></ng-template>
              <ng-template mcCell="actions" let-row>
                <button type="button" mcButton="ghost" size="sm" (click)="openRecipient(r(row))">{{ i18n.t('common.edit') }}</button>
                <button type="button" mcButton="ghost" size="sm" (click)="removeRecipient(r(row))" [attr.data-testid]="'delete-' + r(row).email">{{ i18n.t('common.delete') }}</button>
              </ng-template>
            </mc-data-table>
          </mc-card>
        </div>
      } @else {
        <mc-skeleton [height]="280" />
      }
    } @else if (tab() === 'monitoring') {
      <mc-card [title]="i18n.t('settings.monitoringDefaults')" class="block" data-testid="monitoring-defaults">
        <p class="hint">{{ i18n.t('settings.monitoringHint') }}</p>
        @if (defaults(); as d) {
          <mc-thresholds-form [document]="d" />
          <div class="actions"><button type="button" mcButton="primary-solid" [disabled]="busy()" (click)="saveDefaults()" data-testid="save-defaults">{{ i18n.t('common.save') }}</button></div>
        } @else {
          <mc-skeleton [height]="240" />
        }
      </mc-card>
    } @else if (tab() === 'locations') {
      <mc-card [title]="i18n.t('nav.locations')" [flush]="true" class="block" data-testid="settings-locations">
        <a cardActions mcButton="primary-outline" size="sm" routerLink="../locations">{{ i18n.t('settings.manageLocations') }}</a>
        <mc-data-table [columns]="locationColumns()" [rows]="locations()" [emptyText]="i18n.t('locations.empty')">
          <ng-template mcCell="name" let-row><strong>{{ l(row).isDefault ? i18n.t('locations.unassigned') : l(row).name }}</strong></ng-template>
          <ng-template mcCell="city" let-row>{{ l(row).city || '—' }}</ng-template>
          <ng-template mcCell="devices" let-row>{{ l(row).devices }}</ng-template>
          <ng-template mcCell="actions" let-row>
            <a mcButton="ghost" size="sm" [routerLink]="['../locations', l(row).id, 'settings']" [attr.data-testid]="'location-settings-' + l(row).id">{{ i18n.t('nav.settings') }}</a>
            <a mcButton="ghost" size="sm" [routerLink]="['../locations', l(row).id, 'overview']">{{ i18n.t('common.view') }}</a>
          </ng-template>
        </mc-data-table>
      </mc-card>
    } @else if (tab() === 'integrations') {
      @if (integrations(); as it) {
        <mc-card [title]="i18n.t('settings.webhook')" class="block narrow-card" data-testid="integrations">
          <p class="hint">{{ it.webhookEntitled ? i18n.t('settings.webhookIntegrationHint') : i18n.t('settings.notInPlan') }}</p>
          <form class="mc-form" (ngSubmit)="saveIntegrations()">
            <label class="inline"><input type="checkbox" role="switch" name="webhookEnabled" [(ngModel)]="hook.enabled" [disabled]="!it.webhookEntitled" data-testid="integration-enabled" />
              <span>{{ i18n.t('settings.webhookEnabled') }}</span></label>
            <label><span>{{ i18n.t('settings.webhookUrl') }}</span>
              <input name="webhookUrl" type="url" maxlength="500" placeholder="https://" [(ngModel)]="hook.url" [disabled]="!it.webhookEntitled" data-testid="integration-url" />
            </label>
            <label><span>{{ i18n.t('settings.webhookSecret') }}</span>
              <input name="secret" type="password" maxlength="200" autocomplete="new-password" [(ngModel)]="hook.secret" [disabled]="!it.webhookEntitled"
                [placeholder]="it.hasSecret ? i18n.t('settings.secretStored') : ''" data-testid="integration-secret" />
              <small>{{ i18n.t('settings.secretHint') }}</small>
            </label>
            @if (it.hasSecret) {
              <label class="inline"><input type="checkbox" name="removeSecret" [(ngModel)]="hook.removeSecret" /> <span>{{ i18n.t('settings.removeSecret') }}</span></label>
            }
            @if (testResult(); as r) {
              <p class="test" [class.ok]="r.success" role="status" data-testid="webhook-test-result">
                {{ r.success ? i18n.t('settings.testOk', { status: r.statusCode ?? '' }) : i18n.t('settings.testFailed', { error: r.error ?? '' }) }}
              </p>
            }
            <div class="actions gap">
              <button type="button" mcButton="secondary" [disabled]="busy() || !it.webhookEntitled || !it.webhookUrl" (click)="testWebhook()" data-testid="test-webhook">{{ i18n.t('settings.testWebhook') }}</button>
              <button type="submit" mcButton="primary-solid" [disabled]="busy() || !it.webhookEntitled" data-testid="save-integrations">{{ i18n.t('common.save') }}</button>
            </div>
          </form>
        </mc-card>
      } @else {
        <mc-skeleton [height]="240" />
      }
    }

    <mc-drawer [open]="form() !== null" (openChange)="!$event && form.set(null)" [title]="form()?.id ? i18n.t('settings.editRecipient') : i18n.t('settings.addRecipient')">
      @if (form(); as f) {
        <form class="mc-form" id="recipient-form" (ngSubmit)="saveRecipient()">
          <label><span>{{ i18n.t('users.name') }}</span><input name="name" [(ngModel)]="f.name" required data-testid="recipient-name" /></label>
          <label><span>{{ i18n.t('users.email') }}</span><input name="email" type="email" [(ngModel)]="f.email" required data-testid="recipient-email" /></label>
          <label><span>{{ i18n.t('settings.eventsLabel') }}</span>
            <select name="events" [(ngModel)]="f.events">
              @for (e of events; track e) {
                <option [value]="e">{{ i18n.t('settings.events.' + e) }}</option>
              }
            </select>
          </label>
          <label><span>{{ i18n.t('dashboard.location') }}</span>
            <select name="locationId" [(ngModel)]="f.locationId">
              <option value="">{{ i18n.t('settings.allLocations') }}</option>
              @for (l of locations(); track l.id) {
                <option [value]="l.id">{{ l.name }}</option>
              }
            </select>
          </label>
          @if (f.id) {
            <label class="inline"><input type="checkbox" name="isActive" [(ngModel)]="f.isActive" /> <span>{{ i18n.t('status.active') }}</span></label>
          }
          @if (formError()) {
            <p class="field-error" role="alert">{{ formError() }}</p>
          }
        </form>
      }
      <div drawerActions>
        <button type="button" mcButton="secondary" (click)="form.set(null)">{{ i18n.t('common.cancel') }}</button>
        <button type="submit" form="recipient-form" mcButton="primary-solid" [disabled]="busy()" data-testid="save-recipient">{{ i18n.t('common.save') }}</button>
      </div>
    </mc-drawer>
  `,
  styles: `
    :host { display: block; }
    .block, .grid { margin-block-start: var(--mc-space-4); }
    .grid { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1.4fr); gap: var(--mc-space-4); }
    @media (max-width: 1100px) { .grid { grid-template-columns: 1fr; } }
    .narrow { max-inline-size: 480px; }
    .hint { color: var(--mc-text-muted); margin-block: 0 var(--mc-space-3); }
    .actions { margin-block-start: var(--mc-space-4); display: flex; justify-content: flex-end; }
    .channels { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; }
    .channels li { display: flex; align-items: center; justify-content: space-between; gap: var(--mc-space-4); padding-block: var(--mc-space-3); border-block-end: 1px solid var(--mc-border); }
    .channels li div { display: flex; flex-direction: column; gap: 2px; }
    .channels small { color: var(--mc-text-muted); }
    input[role='switch'] { inline-size: 36px; block-size: 20px; accent-color: var(--mc-brand); }
    .inline { flex-direction: row; align-items: center; gap: var(--mc-space-2); }
    .narrow-card { max-inline-size: 640px; }
    .gap { gap: var(--mc-space-2); }
    .test { margin: 0; padding: var(--mc-space-2) var(--mc-space-3); border-radius: var(--mc-radius-md); background: var(--mc-danger-soft); color: var(--mc-danger); }
    .test.ok { background: var(--mc-success-soft); color: var(--mc-success); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingsPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(SettingsApi);
  private readonly configuration = inject(ConfigurationApi);
  protected readonly defaults = signal<ConfigDocument | null>(null);
  private readonly locationsApi = inject(LocationsApi);
  private readonly toast = inject(ToastService);

  protected readonly severities = ['Critical', 'Warning', 'Info'];
  protected readonly events = EVENTS;
  protected readonly tab = signal('general');
  protected readonly general = signal<GeneralSettings | null>(null);
  protected readonly alerts = signal<AlertSettings | null>(null);
  protected readonly recipients = signal<Recipient[]>([]);
  protected readonly locations = signal<LocationCard[]>([]);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly form = signal<RecipientForm | null>(null);
  protected readonly formError = signal<string | null>(null);
  protected channels: Channels = { emailEnabled: true, inAppEnabled: true, webhookEnabled: false, webhookUrl: '' };
  private readonly integrationsApi = inject(IntegrationsApi);
  protected readonly integrations = signal<Integrations | null>(null);
  protected readonly testResult = signal<WebhookTest | null>(null);
  protected hook = { enabled: false, url: '', secret: '', removeSecret: false };

  protected readonly locationColumns = computed<Column[]>(() => [
    { key: 'name', label: this.i18n.t('locations.name') },
    { key: 'city', label: this.i18n.t('locations.city') },
    { key: 'devices', label: this.i18n.t('locations.devices') },
    { key: 'actions', label: '', width: '180px' },
  ]);

  protected readonly tabs = computed<TabItem[]>(() => [
    { id: 'general', label: this.i18n.t('settings.general') },
    { id: 'alerts', label: this.i18n.t('settings.alertSettings') },
    { id: 'monitoring', label: this.i18n.t('settings.monitoring') },
    { id: 'locations', label: this.i18n.t('nav.locations') },
    { id: 'integrations', label: this.i18n.t('settings.integrations') },
  ]);

  protected readonly recipientColumns = computed<Column[]>(() => [
    { key: 'name', label: this.i18n.t('users.name') },
    { key: 'email', label: this.i18n.t('users.email') },
    { key: 'events', label: this.i18n.t('settings.eventsLabel') },
    { key: 'isActive', label: this.i18n.t('common.status') },
    { key: 'actions', label: '', width: '150px' },
  ]);

  constructor() {
    void this.load();
    void firstValueFrom(this.locationsApi.list({ pageSize: 200 })).then((r) => this.locations.set(r.items)).catch(() => undefined);
  }

  async load(): Promise<void> {
    this.failed.set(false);
    try {
      const [general, alerts, recipients] = await Promise.all([firstValueFrom(this.api.general()), firstValueFrom(this.api.alerts()), firstValueFrom(this.api.recipients())]);
      this.general.set({ ...general });
      this.alerts.set(alerts);
      this.channels = { emailEnabled: alerts.emailEnabled, inAppEnabled: alerts.inAppEnabled, webhookEnabled: alerts.webhookEnabled, webhookUrl: alerts.webhookUrl ?? '' };
      this.recipients.set(recipients);
      this.defaults.set(editable(await firstValueFrom(this.configuration.defaults())));
      this.setIntegrations(await firstValueFrom(this.integrationsApi.get()));
    } catch {
      this.failed.set(true);
    }
  }

  protected async saveDefaults(): Promise<void> {
    const d = this.defaults();
    if (!d) {
      return;
    }
    this.busy.set(true);
    try {
      this.defaults.set(editable(await firstValueFrom(this.configuration.updateDefaults(normalised(d)))));
      this.toast.success(this.i18n.t('settings.saved'));
    } catch {
      // The error toast explains why.
    } finally {
      this.busy.set(false);
    }
  }

  private setIntegrations(value: Integrations): void {
    this.integrations.set(value);
    this.hook = { enabled: value.webhookEnabled, url: value.webhookUrl ?? '', secret: '', removeSecret: false };
  }

  protected l(row: unknown): LocationCard {
    return row as LocationCard;
  }

  protected async saveIntegrations(): Promise<void> {
    this.busy.set(true);
    this.testResult.set(null);
    try {
      const h = this.hook;
      const secret = h.removeSecret ? '' : h.secret === '' ? null : h.secret;
      this.setIntegrations(await firstValueFrom(this.integrationsApi.update({ webhookEnabled: h.enabled, webhookUrl: h.url.trim() || null, secret })));
      this.alerts.set(await firstValueFrom(this.api.alerts()));
      this.toast.success(this.i18n.t('settings.saved'));
    } catch {
      // The error toast explains why.
    } finally {
      this.busy.set(false);
    }
  }

  protected async testWebhook(): Promise<void> {
    this.busy.set(true);
    try {
      this.testResult.set(await firstValueFrom(this.integrationsApi.test()));
    } catch {
      // The error toast explains why.
    } finally {
      this.busy.set(false);
    }
  }

  protected r(row: unknown): Recipient {
    return row as Recipient;
  }

  protected zones(current: string): string[] {
    return TIME_ZONES.includes(current) ? TIME_ZONES : [current, ...TIME_ZONES];
  }

  protected async saveGeneral(): Promise<void> {
    const g = this.general();
    if (!g) {
      return;
    }
    this.busy.set(true);
    try {
      this.general.set({ ...(await firstValueFrom(this.api.updateGeneral({ ...g, offlineAlertDelayMinutes: Number(g.offlineAlertDelayMinutes) }))) });
      this.toast.success(this.i18n.t('settings.saved'));
    } catch {
      // The error toast explains why.
    } finally {
      this.busy.set(false);
    }
  }

  protected async saveChannels(): Promise<void> {
    this.busy.set(true);
    try {
      const c = this.channels;
      await firstValueFrom(this.api.updateAlerts({ emailEnabled: c.emailEnabled, inAppEnabled: c.inAppEnabled, webhookEnabled: c.webhookEnabled, webhookUrl: c.webhookEnabled ? c.webhookUrl : null }));
      this.alerts.set(await firstValueFrom(this.api.alerts()));
      this.toast.success(this.i18n.t('settings.saved'));
    } catch {
      // The error toast explains why.
    } finally {
      this.busy.set(false);
    }
  }

  protected openRecipient(recipient: Recipient | null): void {
    this.formError.set(null);
    this.form.set(
      recipient
        ? { id: recipient.id, name: recipient.name, email: recipient.email, events: recipient.events, locationId: recipient.locationId ?? '', isActive: recipient.isActive }
        : { id: null, name: '', email: '', events: 'All', locationId: '', isActive: true },
    );
  }

  protected async saveRecipient(): Promise<void> {
    const f = this.form();
    if (!f) {
      return;
    }
    this.busy.set(true);
    this.formError.set(null);
    const body = { name: f.name, email: f.email, events: f.events, locationId: f.locationId || null };
    try {
      if (f.id) {
        await firstValueFrom(this.api.updateRecipient(f.id, { ...body, isActive: f.isActive }));
      } else {
        await firstValueFrom(this.api.addRecipient(body));
      }
      this.form.set(null);
      this.recipients.set(await firstValueFrom(this.api.recipients()));
    } catch (e) {
      this.formError.set(this.i18n.errorMessage(toProblem(e).code));
    } finally {
      this.busy.set(false);
    }
  }

  protected async removeRecipient(recipient: Recipient): Promise<void> {
    try {
      await firstValueFrom(this.api.deleteRecipient(recipient.id));
      this.recipients.update((list) => list.filter((r) => r.id !== recipient.id));
    } catch {
      // The error toast explains why.
    }
  }
}
