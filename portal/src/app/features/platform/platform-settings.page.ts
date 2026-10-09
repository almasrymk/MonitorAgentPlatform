import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';

import { toProblem } from '../../core/api/problem';
import { PlatformSettings, PlatformSettingsApi } from '../../core/api/reports.api';
import { I18nService } from '../../core/i18n/i18n.service';
import { ToastService } from '../../core/ui/toast.service';
import { Button } from '../../shared/ui/button';
import { Card } from '../../shared/ui/card';
import { PageHeader } from '../../shared/ui/headers';
import { Skeleton } from '../../shared/ui/skeleton';
import { ErrorState } from '../../shared/ui/states';

/** Platform Settings (06: /platform/settings): branding name, default offline-alert delay, telemetry retention, e-mail sender. */
@Component({
  selector: 'mc-platform-settings-page',
  imports: [FormsModule, PageHeader, Card, Button, Skeleton, ErrorState],
  template: `
    <mc-page-header [title]="i18n.t('nav.settings')" [subtitle]="i18n.t('platformSettings.subtitle')" />
    @if (failed()) {
      <mc-error-state (retry)="load()" />
    } @else if (form(); as f) {
      <form class="columns" (ngSubmit)="save()" data-testid="platform-settings">
        <mc-card [title]="i18n.t('platformSettings.branding')">
          <div class="mc-form">
            <label><span>{{ i18n.t('platformSettings.brandName') }}</span><input name="brandName" required maxlength="100" [(ngModel)]="f.brandName" data-testid="brand-name" /></label>
            <label><span>{{ i18n.t('platformSettings.senderName') }}</span><input name="emailSenderName" required maxlength="100" [(ngModel)]="f.emailSenderName" /></label>
            <label><span>{{ i18n.t('platformSettings.senderAddress') }}</span><input name="emailSenderAddress" type="email" required maxlength="256" [(ngModel)]="f.emailSenderAddress" /></label>
          </div>
        </mc-card>
        <mc-card [title]="i18n.t('platformSettings.monitoring')">
          <div class="mc-form">
            <label><span>{{ i18n.t('settings.offlineDelay') }}</span>
              <input name="offlineAlertDelayMinutes" type="number" min="1" max="60" required [(ngModel)]="f.offlineAlertDelayMinutes" />
              <small>{{ i18n.t('platformSettings.offlineDelayHint') }}</small>
            </label>
            <label><span>{{ i18n.t('platformSettings.minuteRetention') }}</span>
              <input name="minuteRetentionDays" type="number" min="7" max="90" required [(ngModel)]="f.minuteRetentionDays" />
            </label>
            <label><span>{{ i18n.t('platformSettings.hourRetention') }}</span>
              <input name="hourRetentionDays" type="number" min="90" max="1100" required [(ngModel)]="f.hourRetentionDays" />
            </label>
          </div>
        </mc-card>
        @if (error()) {
          <p class="field-error" role="alert">{{ error() }}</p>
        }
        <div class="actions"><button type="submit" mcButton="primary-solid" [disabled]="busy()" data-testid="save-platform-settings">{{ i18n.t('common.save') }}</button></div>
      </form>
    } @else {
      <mc-skeleton [height]="320" />
    }
  `,
  styles: `
    :host { display: block; }
    .columns { display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: var(--mc-space-4); }
    .actions { grid-column: 1 / -1; display: flex; justify-content: flex-end; }
    .field-error { grid-column: 1 / -1; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlatformSettingsPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(PlatformSettingsApi);
  private readonly toast = inject(ToastService);

  protected readonly form = signal<PlatformSettings | null>(null);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    void this.load();
  }

  async load(): Promise<void> {
    this.failed.set(false);
    try {
      this.form.set({ ...(await firstValueFrom(this.api.get())) });
    } catch {
      this.failed.set(true);
    }
  }

  protected async save(): Promise<void> {
    const f = this.form();
    if (!f) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      const body = { ...f, offlineAlertDelayMinutes: Number(f.offlineAlertDelayMinutes), minuteRetentionDays: Number(f.minuteRetentionDays), hourRetentionDays: Number(f.hourRetentionDays) };
      this.form.set({ ...(await firstValueFrom(this.api.update(body))) });
      this.toast.success(this.i18n.t('settings.saved'));
    } catch (e) {
      this.error.set(this.i18n.errorMessage(toProblem(e).code));
    } finally {
      this.busy.set(false);
    }
  }
}
