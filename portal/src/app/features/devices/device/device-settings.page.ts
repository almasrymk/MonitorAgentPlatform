import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';

import { ConfigDocument, ConfigurationApi, DeviceConfiguration } from '../../../core/api/monitoring.api';
import { toProblem } from '../../../core/api/problem';
import { AuthService } from '../../../core/auth/auth.service';
import { I18nService } from '../../../core/i18n/i18n.service';
import { LiveService } from '../../../core/live/live.service';
import { ToastService } from '../../../core/ui/toast.service';
import { Button } from '../../../shared/ui/button';
import { Card } from '../../../shared/ui/card';
import { Skeleton } from '../../../shared/ui/skeleton';
import { ErrorState } from '../../../shared/ui/states';
import { StatusPill } from '../../../shared/ui/status-pill';
import { ThresholdsForm, editable, normalised } from '../../configuration/thresholds-form';
import { DeviceContext } from './device-context';

/** Device Settings tab (07 section 5.7): thresholds and intervals, applied vs target configuration version, licence state. */
@Component({
  selector: 'mc-device-settings-page',
  imports: [DatePipe, Card, ThresholdsForm, Button, Skeleton, ErrorState, StatusPill],
  template: `
    @if (failed()) {
      <mc-error-state (retry)="load()" />
    } @else if (config(); as c) {
      <div class="grid">
        <mc-card [title]="i18n.t('config.thresholds')" data-testid="device-thresholds">
          @if (document(); as d) {
            <mc-thresholds-form [document]="d" [readonly]="!canEdit()" />
          }
          @if (canEdit()) {
            <div class="actions"><button type="button" mcButton="primary-solid" [disabled]="busy()" (click)="save()" data-testid="save-configuration">{{ i18n.t('common.save') }}</button></div>
          }
        </mc-card>
        <mc-card [title]="i18n.t('config.version')" data-testid="config-version">
          <dl class="kv">
            <dt>{{ i18n.t('config.target') }}</dt><dd data-testid="target-version">{{ c.version || '—' }}</dd>
            <dt>{{ i18n.t('config.applied') }}</dt><dd data-testid="applied-version">{{ c.appliedVersion || '—' }}@if (c.appliedAt) { · {{ c.appliedAt | date: 'short' : undefined : locale() }} }</dd>
            <dt>{{ i18n.t('common.status') }}</dt>
            <dd>
              @if (c.rejectedVersion) {
                <mc-status-pill status="critical" [label]="i18n.t('config.rejected', { version: c.rejectedVersion })" data-testid="config-status" />
              } @else if (c.version > 0 && c.appliedVersion === c.version) {
                <mc-status-pill status="healthy" [label]="i18n.t('config.inSync')" data-testid="config-status" />
              } @else {
                <mc-status-pill status="warning" [label]="i18n.t('config.pending')" data-testid="config-status" />
              }
            </dd>
            @if (c.error) {
              <dt>{{ i18n.t('config.error') }}</dt><dd class="error" data-testid="config-error">{{ c.error }}</dd>
            }
            <dt>{{ i18n.t('devices.license') }}</dt>
            <dd>@if (context.device(); as dev) { <mc-status-pill [status]="dev.licenseState" /> @if (dev.licenseReason) { ({{ dev.licenseReason }}) } }</dd>
          </dl>
        </mc-card>
      </div>
    } @else {
      <mc-skeleton [height]="320" />
    }
  `,
  styles: `
    .grid { display: grid; grid-template-columns: minmax(0, 2fr) minmax(0, 1fr); gap: var(--mc-space-4); }
    @media (max-width: 1100px) { .grid { grid-template-columns: 1fr; } }
    .actions { display: flex; justify-content: flex-end; margin-block-start: var(--mc-space-4); }
    .kv { display: grid; grid-template-columns: max-content 1fr; gap: var(--mc-space-2) var(--mc-space-4); margin: 0; }
    dt { color: var(--mc-text-muted); }
    dd { margin: 0; }
    .error { color: var(--mc-danger); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DeviceSettingsPage {
  protected readonly i18n = inject(I18nService);
  protected readonly context = inject(DeviceContext);
  private readonly api = inject(ConfigurationApi);
  private readonly toast = inject(ToastService);
  private readonly auth = inject(AuthService);

  protected readonly config = signal<DeviceConfiguration | null>(null);
  protected readonly document = signal<ConfigDocument | null>(null);
  protected readonly failed = signal(false);
  protected readonly busy = signal(false);
  protected readonly canEdit = computed(() => this.auth.hasPermission('devices.configure'));
  protected readonly locale = computed(() => (this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB'));

  constructor() {
    effect(() => {
      if (this.context.id()) {
        untracked(() => void this.load());
      }
    });
    inject(LiveService)
      .configApplied$.pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe((e) => {
        if (e.deviceId === this.context.id()) {
          void this.load(false);
        }
      });
  }

  /** resetForm: false keeps what the user is typing and refreshes only the version block. */
  async load(resetForm = true): Promise<void> {
    this.failed.set(false);
    try {
      const config = await firstValueFrom(this.api.get(this.context.id()));
      this.config.set(config);
      if (resetForm || !this.document()) {
        this.document.set(editable(config.document));
      }
    } catch {
      this.failed.set(true);
    }
  }

  protected async save(): Promise<void> {
    const document = this.document();
    const current = this.config();
    if (!document || !current) {
      return;
    }
    this.busy.set(true);
    try {
      const saved = await firstValueFrom(this.api.update(this.context.id(), normalised(document), current.version));
      this.config.set(saved);
      this.document.set(editable(saved.document));
      this.toast.success(this.i18n.t('config.saved', { version: saved.version }));
    } catch (e) {
      if (toProblem(e).code === 'CONCURRENCY_CONFLICT') {
        await this.load();
      }
    } finally {
      this.busy.set(false);
    }
  }
}
