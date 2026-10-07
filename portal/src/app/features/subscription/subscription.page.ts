import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { LicensingApi, Subscription } from '../../core/api/licensing.api';
import { I18nService } from '../../core/i18n/i18n.service';
import { Button } from '../../shared/ui/button';
import { Card } from '../../shared/ui/card';
import { Dialog } from '../../shared/ui/dialog';
import { PageHeader } from '../../shared/ui/headers';
import { Skeleton } from '../../shared/ui/skeleton';
import { EmptyState, ErrorState } from '../../shared/ui/states';
import { StatusPill } from '../../shared/ui/status-pill';
import { TabItem, Tabs } from '../../shared/ui/tabs';
import { UsageBar } from '../../shared/ui/usage-bar';

/** Subscription & Licenses (07 section 5.8). Device tabs fill in with the Devices module (M3). */
@Component({
  selector: 'mc-subscription-page',
  imports: [DatePipe, PageHeader, Tabs, Card, StatusPill, UsageBar, Button, Dialog, Skeleton, EmptyState, ErrorState],
  templateUrl: './subscription.page.html',
  styleUrl: './subscription.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SubscriptionPage {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(LicensingApi);

  protected readonly subscription = signal<Subscription | null>(null);
  protected readonly loading = signal(true);
  protected readonly failed = signal(false);
  protected readonly tab = signal('overview');
  protected readonly contactOpen = signal(false);

  protected readonly tabs = computed<TabItem[]>(() => {
    const s = this.subscription();
    const licensed = s?.licensedDevices ?? 0;
    const unlicensed = s?.unlicensedDevices ?? 0;
    return [
      { id: 'overview', label: this.i18n.t('subscription.tabOverview') },
      { id: 'all', label: this.i18n.t('subscription.tabAll'), count: licensed + unlicensed },
      { id: 'licensed', label: this.i18n.t('subscription.tabLicensed'), count: licensed },
      { id: 'unlicensed', label: this.i18n.t('subscription.tabUnlicensed'), count: unlicensed },
    ];
  });

  /** Status pill: Expiring wins over Active when the renewal is within 30 days. */
  protected readonly pill = computed(() => {
    const s = this.subscription();
    if (!s) {
      return 'unknown';
    }
    return s.expiringSoon ? 'expiring' : s.status.toLowerCase();
  });

  /** Licensed share for the donut (0-100). */
  protected readonly licensedPercent = computed(() => {
    const s = this.subscription();
    const total = (s?.licensedDevices ?? 0) + (s?.unlicensedDevices ?? 0);
    return total === 0 ? 0 : Math.round(((s?.licensedDevices ?? 0) / total) * 100);
  });

  constructor() {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.failed.set(false);
    try {
      this.subscription.set(await firstValueFrom(this.api.subscription()));
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  protected locale(): string {
    return this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB';
  }
}
