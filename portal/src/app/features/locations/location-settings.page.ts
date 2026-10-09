import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

import { LocationsApi } from '../../core/api/api.services';
import { Location, LocationRequest } from '../../core/api/models';
import { toProblem } from '../../core/api/problem';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { ToastService } from '../../core/ui/toast.service';
import { Button } from '../../shared/ui/button';
import { Card } from '../../shared/ui/card';
import { Skeleton } from '../../shared/ui/skeleton';
import { ErrorState } from '../../shared/ui/states';
import { LocationContext } from './location-context';

const TIME_ZONES = ['Africa/Cairo', 'Asia/Dubai', 'Asia/Riyadh', 'Asia/Kuwait', 'Asia/Qatar', 'Asia/Amman', 'Europe/London', 'Europe/Berlin', 'UTC'];

type LocationForm = { [K in keyof LocationRequest]: string };

/** Location > Settings: the location's details (name, code, address, time zone, contact). */
@Component({
  selector: 'mc-location-settings-page',
  imports: [FormsModule, Card, Button, Skeleton, ErrorState],
  template: `
    @if (failed()) {
      <mc-error-state (retry)="load(context.id())" />
    } @else if (form(); as f) {
      <form (ngSubmit)="save()" class="columns" data-testid="location-settings">
        <mc-card [title]="i18n.t('locationSettings.details')">
          <fieldset class="mc-form" [disabled]="!canManage()">
            <label><span>{{ i18n.t('locations.name') }}</span><input name="name" required maxlength="200" [(ngModel)]="f.name" [readonly]="isDefault()" data-testid="location-name" /></label>
            <label><span>{{ i18n.t('locations.code') }}</span><input name="code" required maxlength="32" [(ngModel)]="f.code" [readonly]="isDefault()" /></label>
            <label><span>{{ i18n.t('locations.timeZone') }}</span>
              <select name="timeZone" [(ngModel)]="f.timeZone">
                @for (zone of zones(f.timeZone); track zone) { <option [value]="zone">{{ zone }}</option> }
              </select>
            </label>
          </fieldset>
        </mc-card>
        <mc-card [title]="i18n.t('locationSettings.address')">
          <fieldset class="mc-form" [disabled]="!canManage()">
            <label><span>{{ i18n.t('locations.country') }}</span><input name="country" maxlength="100" [(ngModel)]="f.country" /></label>
            <label><span>{{ i18n.t('locations.city') }}</span><input name="city" maxlength="100" [(ngModel)]="f.city" /></label>
            <label><span>{{ i18n.t('locations.address') }}</span><input name="addressLine" maxlength="300" [(ngModel)]="f.addressLine" /></label>
          </fieldset>
        </mc-card>
        <mc-card [title]="i18n.t('dashboard.contact')">
          <fieldset class="mc-form" [disabled]="!canManage()">
            <label><span>{{ i18n.t('locations.contactName') }}</span><input name="contactName" maxlength="200" [(ngModel)]="f.contactName" /></label>
            <label><span>{{ i18n.t('locations.contactEmail') }}</span><input name="contactEmail" type="email" maxlength="256" [(ngModel)]="f.contactEmail" /></label>
            <label><span>{{ i18n.t('locations.contactPhone') }}</span><input name="contactPhone" maxlength="50" [(ngModel)]="f.contactPhone" /></label>
          </fieldset>
        </mc-card>
        @if (error()) {
          <p class="field-error" role="alert">{{ error() }}</p>
        }
        @if (canManage()) {
          <div class="actions"><button type="submit" mcButton="primary-solid" [disabled]="busy()" data-testid="save-location">{{ i18n.t('common.save') }}</button></div>
        }
      </form>
    } @else {
      <mc-skeleton [height]="320" />
    }
  `,
  styles: `
    :host { display: block; }
    .columns { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: var(--mc-space-4); }
    fieldset { border: 0; padding: 0; margin: 0; min-inline-size: 0; }
    .actions, .field-error { grid-column: 1 / -1; }
    .actions { display: flex; justify-content: flex-end; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LocationSettingsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly context = inject(LocationContext);
  private readonly api = inject(LocationsApi);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  private readonly location = signal<Location | null>(null);
  protected readonly form = signal<LocationForm | null>(null);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly canManage = computed(() => this.auth.hasPermission('locations.manage'));
  protected readonly isDefault = computed(() => this.location()?.isDefault ?? false);

  constructor() {
    effect(() => {
      const id = this.context.id();
      if (id) {
        untracked(() => void this.load(id));
      }
    });
  }

  async load(id: string): Promise<void> {
    this.failed.set(false);
    try {
      const l = await firstValueFrom(this.api.get(id));
      this.location.set(l);
      this.form.set({
        name: l.name,
        code: l.code,
        city: l.city ?? '',
        country: l.country ?? '',
        addressLine: l.addressLine ?? '',
        timeZone: l.timeZone,
        contactName: l.contactName ?? '',
        contactEmail: l.contactEmail ?? '',
        contactPhone: l.contactPhone ?? '',
      });
    } catch {
      this.failed.set(true);
    }
  }

  protected zones(current: string): string[] {
    return TIME_ZONES.includes(current) ? TIME_ZONES : [current, ...TIME_ZONES];
  }

  protected async save(): Promise<void> {
    const f = this.form();
    const l = this.location();
    if (!f || !l) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    const value = (s: string): string | null => (s.trim() === '' ? null : s.trim());
    try {
      const body: LocationRequest = {
        name: value(f.name),
        code: value(f.code),
        city: value(f.city),
        country: value(f.country),
        addressLine: value(f.addressLine),
        timeZone: value(f.timeZone),
        contactName: value(f.contactName),
        contactEmail: value(f.contactEmail),
        contactPhone: value(f.contactPhone),
      };
      await firstValueFrom(this.api.update(l.id, body, l.version));
      this.toast.success(this.i18n.t('settings.saved'));
      await Promise.all([this.load(l.id), this.context.load(l.id, true)]);
    } catch (e) {
      this.error.set(this.i18n.errorMessage(toProblem(e).code));
    } finally {
      this.busy.set(false);
    }
  }
}
