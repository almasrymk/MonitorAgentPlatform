import { DestroyRef, Injectable, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter, firstValueFrom, map } from 'rxjs';

import { NotificationsApi } from '../api/monitoring.api';
import { AuthService } from '../auth/auth.service';
import { areaRoot } from '../layout/area';
import { LiveService } from '../live/live.service';
import { ScopeStore } from './scope.store';

/**
 * The bell: unread notifications of the current feed (platform feed in `/admin`, the customer feed in `/app` and
 * in a workspace). Refreshed on navigation into another area, on `notificationCreated` and after "mark as read".
 */
@Injectable({ providedIn: 'root' })
export class NotificationsStore {
  private readonly api = inject(NotificationsApi);
  private readonly auth = inject(AuthService);
  private readonly live = inject(LiveService);
  private readonly scope = inject(ScopeStore);
  private readonly router = inject(Router);
  private release: (() => void) | null = null;

  readonly unread = signal(0);
  private readonly url = toSignal(this.router.events.pipe(filter((e) => e instanceof NavigationEnd), map(() => this.router.url)), { initialValue: this.router.url });

  /** The area root the bell belongs to, e.g. `/admin` or `/app`. */
  readonly root = computed(() => areaRoot(this.url()));
  readonly platform = computed(() => this.root() === '/admin');

  constructor() {
    effect(() => {
      const signedIn = this.auth.isSignedIn();
      const platform = this.platform();
      const workspace = this.scope.workspace()?.id ?? null;
      this.release?.();
      this.release = null;
      if (!signedIn || this.url().startsWith('/login')) {
        this.unread.set(0);
        return;
      }
      this.release = this.live.subscribe(platform ? { kind: 'platform' } : { kind: 'tenant', id: workspace });
      void this.refresh();
    });
    this.live.notification$.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe(() => void this.refresh());
  }

  async refresh(): Promise<void> {
    if (!this.auth.isSignedIn()) {
      return;
    }
    const permission = this.platform() ? 'platform.dashboard.read' : 'notifications.read';
    if (!this.auth.hasPermission(permission)) {
      this.unread.set(0);
      return;
    }
    try {
      this.unread.set((await firstValueFrom(this.api.unread(this.platform()))).unread);
    } catch {
      // Keep the last count.
    }
  }
}
