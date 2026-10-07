import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { LicensingApi, LicensingStatus, Plan } from '../../core/api/licensing.api';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { ToastService } from '../../core/ui/toast.service';
import { Button } from '../../shared/ui/button';
import { Card } from '../../shared/ui/card';
import { CellDef, Column, DataTable } from '../../shared/ui/data-table';
import { PageHeader } from '../../shared/ui/headers';
import { ErrorState } from '../../shared/ui/states';
import { StatusPill } from '../../shared/ui/status-pill';

/** Plans (platform, read-only; 04 section 5): plans live in the Licensing Platform. */
@Component({
  selector: 'mc-plans-page',
  imports: [DatePipe, PageHeader, Card, DataTable, CellDef, Button, StatusPill, ErrorState],
  template: `
    <mc-page-header [title]="i18n.t('plans.title')" [subtitle]="i18n.t('plans.subtitle')">
      @if (status()?.portalUrl; as url) {
        <a mcButton="secondary" [href]="url" target="_blank" rel="noopener" data-testid="open-licensing">{{ i18n.t('plans.openLicensing') }}</a>
      }
      @if (canSync()) {
        <button type="button" mcButton="primary-outline" [disabled]="syncing()" data-testid="sync" (click)="sync()">{{ i18n.t('plans.syncNow') }}</button>
      }
    </mc-page-header>

    @if (status(); as s) {
      <p class="status" data-testid="licensing-status">
        <mc-status-pill [status]="s.degraded ? 'warning' : 'active'" [label]="i18n.t(s.degraded ? 'plans.syncDegraded' : 'plans.syncOk')" />
        {{ i18n.t('plans.mode', { mode: s.mode }) }}
        @if (s.lastSuccessAt) {
          · {{ i18n.t('plans.lastSync', { at: (s.lastSuccessAt | date: 'medium' : undefined : locale()) ?? '' }) }}
        }
      </p>
    }

    @if (failed()) {
      <mc-error-state (retry)="load()" />
    } @else {
      <mc-card [flush]="true">
        <mc-data-table [columns]="columns()" [rows]="plans()" [loading]="loading()" [trackBy]="trackByCode" [emptyText]="i18n.t('plans.empty')">
          <ng-template mcCell="name" let-row><strong>{{ $any(row).name }}</strong> <span class="code">{{ $any(row).code }}</span></ng-template>
          <ng-template mcCell="deviceLimit" let-row>{{ $any(row).deviceLimit ?? '∞' }}</ng-template>
          <ng-template mcCell="features" let-row><span class="features">{{ $any(row).features.join(', ') }}</span></ng-template>
          <ng-template mcCell="price" let-row>{{ $any(row).price }} {{ $any(row).currency }}</ng-template>
        </mc-data-table>
      </mc-card>
    }
  `,
  styles: `
    .status { display: flex; align-items: center; gap: var(--mc-space-2); color: var(--mc-text-muted); margin: 0 0 var(--mc-space-4); }
    .code { color: var(--mc-text-faint); font-family: var(--mc-font-mono); font-size: var(--mc-fs-xs); }
    .features { color: var(--mc-text-secondary); }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlansPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(LicensingApi);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  protected readonly plans = signal<Plan[]>([]);
  protected readonly status = signal<LicensingStatus | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly syncing = signal(false);
  protected readonly canSync = computed(() => this.auth.hasPermission('platform.licensing.sync'));
  protected readonly trackByCode = (row: Plan) => row.code;

  protected readonly columns = computed<Column[]>(() => [
    { key: 'name', label: this.i18n.t('plans.plan') },
    { key: 'deviceLimit', label: this.i18n.t('plans.deviceLimit') },
    { key: 'features', label: this.i18n.t('plans.features') },
    { key: 'price', label: this.i18n.t('plans.price') },
    { key: 'customers', label: this.i18n.t('plans.customers') },
  ]);

  constructor() {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);
    try {
      const [plans, status] = await Promise.all([firstValueFrom(this.api.plans()), firstValueFrom(this.api.status())]);
      this.plans.set(plans);
      this.status.set(status);
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  async sync(): Promise<void> {
    this.syncing.set(true);
    try {
      const outcome = await firstValueFrom(this.api.sync());
      this.toast.success(this.i18n.t('plans.synced', { checked: outcome.tenantsChecked, changed: outcome.tenantsChanged }));
      await this.load();
    } catch {
      // Shown by the error interceptor.
    } finally {
      this.syncing.set(false);
    }
  }

  protected locale(): string {
    return this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB';
  }
}
