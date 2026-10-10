import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { DevicesApi } from '../../../core/api/api.services';
import { I18nService } from '../../../core/i18n/i18n.service';
import { areaRoot } from '../../../core/layout/area';
import { BreadcrumbLabels } from '../../../core/layout/breadcrumb-labels';
import { LiveService } from '../../../core/live/live.service';
import { relativeTime, uptime } from '../../../shared/format';
import { CopyButton } from '../../../shared/ui/copy-button';
import { OsIcon } from '../../../shared/ui/os-icon';
import { Skeleton } from '../../../shared/ui/skeleton';
import { ErrorState } from '../../../shared/ui/states';
import { StatusPill } from '../../../shared/ui/status-pill';
import { DeviceContext } from './device-context';
import { RemoteActions } from './remote-actions';

/** How often the open device screen renews live mode (05 section 4). */
export const LIVE_RENEW_MS = 30_000;

/**
 * Device details (07 section 5.7): header; the tabs of 07 section 5.7 are the device menu of the sidebar. While the screen is visible and the device online it keeps live
 * mode on (`POST live-sessions` now and every 30 s) and shows `liveSample`s; `snapshotUpdated` refetches the overview.
 */
@Component({
  selector: 'mc-device-page',
  imports: [DatePipe, RouterOutlet, RouterLink, OsIcon, StatusPill, CopyButton, Skeleton, ErrorState, RemoteActions],
  providers: [DeviceContext],
  templateUrl: './device.page.html',
  styleUrl: './device.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DevicePage {
  protected readonly i18n = inject(I18nService);
  protected readonly context = inject(DeviceContext);
  private readonly api = inject(DevicesApi);
  private readonly router = inject(Router);
  readonly deviceId = input.required<string>();

  protected readonly root = computed(() => areaRoot(this.router.url));
  protected readonly locale = computed(() => (this.i18n.language() === 'ar' ? 'ar-EG' : 'en-GB'));
  protected readonly status = computed(() => {
    const d = this.context.device();
    return !d ? 'unknown' : d.connection === 'Online' ? (d.health === 'Unknown' ? 'online' : d.health) : 'offline';
  });
  protected readonly lastSeen = computed(() => relativeTime(this.i18n, this.context.device()?.lastSeenAt));
  protected readonly up = computed(() => uptime(this.i18n, this.context.device()?.uptimeSeconds));

  constructor() {
    const live = inject(LiveService);
    const labels = inject(BreadcrumbLabels);
    const destroy = inject(DestroyRef);
    effect(() => void this.context.load(this.deviceId()));
    effect((onCleanup) => {
      const id = this.deviceId();
      onCleanup(untracked(() => live.subscribe({ kind: 'device', id })));
    });
    effect(() => {
      const d = this.context.device();
      if (d) {
        labels.set(':device', d.name);
      }
    });

    // Live mode only while the device is online and the screen visible.
    effect((onCleanup) => {
      if (!this.context.online()) {
        return;
      }
      const id = this.deviceId();
      const renew = () => {
        if (typeof document === 'undefined' || document.visibilityState !== 'hidden') {
          void firstValueFrom(this.api.liveSession(id)).catch(() => undefined);
        }
      };
      untracked(renew);
      const timer = setInterval(renew, LIVE_RENEW_MS);
      onCleanup(() => clearInterval(timer));
    });

    live.liveSample$.pipe(takeUntilDestroyed(destroy)).subscribe((sample) => {
      if (sample.deviceId === this.deviceId()) {
        this.context.live.set(sample);
      }
    });
    live.snapshot$.pipe(takeUntilDestroyed(destroy)).subscribe((event) => {
      if (event.deviceId === this.deviceId()) {
        void this.context.reloadOverview();
      }
    });
    live.deviceState$.pipe(takeUntilDestroyed(destroy)).subscribe((event) => {
      if (event.deviceId === this.deviceId()) {
        void this.context.reloadDevice();
      }
    });
  }
}
