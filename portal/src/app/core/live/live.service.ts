import { DestroyRef, Injectable, effect, inject, signal } from '@angular/core';
import { Subject } from 'rxjs';

import type { HubConnection } from '@microsoft/signalr';

import { AuthService } from '../auth/auth.service';

/** `deviceStateChanged` (06 section 5). */
export interface DeviceStateEvent {
  deviceId: string;
  locationId: string;
  connection: string;
  health: string;
  licenseState: string;
  cpu: number | null;
  ram: number | null;
  disk: number | null;
  lastSeenAt: string | null;
}

/** `summaryChanged`: a hint to refetch the composite of a scope (coalesced by the server). */
export interface SummaryEvent {
  scope: 'platform' | 'tenant' | 'location';
  id: string | null;
}

export type LiveTarget = { kind: 'platform' } | { kind: 'tenant'; id?: string | null } | { kind: 'location'; id: string } | { kind: 'device'; id: string };

const METHODS = {
  platform: ['SubscribePlatform', null],
  tenant: ['SubscribeTenant', null],
  location: ['SubscribeLocation', 'UnsubscribeLocation'],
  device: ['SubscribeDevice', 'UnsubscribeDevice'],
} as const;

/**
 * The portal's connection to the live hub `/hubs/live`. It connects on the first subscription (the SignalR client
 * is loaded on demand), counts subscriptions per group, and joins them again after a reconnect.
 */
@Injectable({ providedIn: 'root' })
export class LiveService {
  private readonly auth = inject(AuthService);
  private connection: HubConnection | null = null;
  private starting: Promise<HubConnection | null> | null = null;
  private readonly counts = new Map<string, { target: LiveTarget; count: number }>();

  readonly deviceState$ = new Subject<DeviceStateEvent>();
  readonly summary$ = new Subject<SummaryEvent>();
  readonly connected = signal(false);

  constructor() {
    // Signing out closes the connection; the next subscription opens a new one with the new token.
    effect(() => {
      if (!this.auth.isSignedIn()) {
        void this.stop();
      }
    });
  }

  /** Joins a group for as long as the calling component lives. */
  watch(target: LiveTarget, destroyRef: DestroyRef): void {
    const release = this.subscribe(target);
    destroyRef.onDestroy(release);
  }

  /** Joins a group; the returned function leaves it. */
  subscribe(target: LiveTarget): () => void {
    const key = LiveService.key(target);
    const entry = this.counts.get(key);
    if (entry) {
      entry.count++;
    } else {
      this.counts.set(key, { target, count: 1 });
      void this.join(target);
    }
    let released = false;
    return () => {
      if (released) {
        return;
      }
      released = true;
      const current = this.counts.get(key);
      if (current && --current.count === 0) {
        this.counts.delete(key);
        void this.leave(target);
      }
    };
  }

  static key(target: LiveTarget): string {
    return 'id' in target && target.id ? `${target.kind}:${target.id}` : target.kind;
  }

  private async join(target: LiveTarget): Promise<void> {
    const connection = await this.ensureConnected();
    if (!connection) {
      return;
    }
    try {
      const [method] = METHODS[target.kind];
      // Hub methods take exactly their parameters: SubscribeTenant always gets its (nullable) tenant id.
      await (target.kind === 'platform' ? connection.invoke(method) : connection.invoke(method, ('id' in target ? target.id : null) ?? null));
    } catch {
      // Not allowed or not connected: the screen still works without live updates.
    }
  }

  private async leave(target: LiveTarget): Promise<void> {
    const [, method] = METHODS[target.kind];
    if (!method || !this.connection || !('id' in target)) {
      return;
    }
    try {
      await this.connection.invoke(method, target.id);
    } catch {
      // The connection may be gone.
    }
  }

  private ensureConnected(): Promise<HubConnection | null> {
    if (this.connection) {
      return Promise.resolve(this.connection);
    }
    if (!this.auth.accessToken) {
      return Promise.resolve(null);
    }
    this.starting ??= this.start().finally(() => (this.starting = null));
    return this.starting;
  }

  private async start(): Promise<HubConnection | null> {
    try {
      const signalr = await import('@microsoft/signalr');
      const connection = new signalr.HubConnectionBuilder()
        .withUrl('/hubs/live', { accessTokenFactory: () => this.auth.accessToken ?? '' })
        .withAutomaticReconnect()
        .configureLogging(signalr.LogLevel.None)
        .build();
      connection.on('deviceStateChanged', (event: DeviceStateEvent) => this.deviceState$.next(event));
      connection.on('summaryChanged', (event: SummaryEvent) => this.summary$.next(event));
      connection.onreconnected(() => {
        this.connected.set(true);
        for (const { target } of this.counts.values()) {
          void this.join(target);
        }
      });
      connection.onreconnecting(() => this.connected.set(false));
      connection.onclose(() => {
        this.connected.set(false);
        this.connection = null;
      });
      await connection.start();
      this.connection = connection;
      this.connected.set(true);
      return connection;
    } catch {
      this.connected.set(false);
      return null;
    }
  }

  private async stop(): Promise<void> {
    const connection = this.connection;
    this.connection = null;
    this.counts.clear();
    this.connected.set(false);
    try {
      await connection?.stop();
    } catch {
      // Already stopped.
    }
  }
}
