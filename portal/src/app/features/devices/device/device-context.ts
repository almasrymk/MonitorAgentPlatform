import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { DevicesApi } from '../../../core/api/api.services';
import { DeviceDetails, DeviceDisk, DeviceOverview } from '../../../core/api/models';
import { LiveSampleEvent } from '../../../core/live/live.service';

/** The snapshot document of 05 section 5 (only the fields the screen reads). */
export interface SnapshotDoc {
  capturedAt?: string;
  cpu?: { usage?: number; physicalCores?: number; logicalCores?: number; speedGhz?: number; tempC?: number; processes?: number; model?: string };
  ram?: { usage?: number; totalGb?: number; usedGb?: number; freeGb?: number; cachedGb?: number };
  diskActivity?: { activePercent?: number; readBps?: number; writeBps?: number; responseMs?: number };
  partitions?: { drive: string; totalGb?: number; usedGb?: number; freeGb?: number; usage?: number }[];
  network?: { adapter?: string; downloadBps?: number; uploadBps?: number; publicIp?: string; localIp?: string; pingMs?: number; lossPercent?: number; lastSpeedTest?: { downloadMbps?: number; uploadMbps?: number; at?: string } };
  top?: Record<'cpu' | 'ram' | 'disk' | 'network', { name: string; pid: number; value: number }[]>;
  service?: { status?: string; uptimeSeconds?: number };
}

/** The device being viewed, shared by the device page and its tabs. */
@Injectable()
export class DeviceContext {
  private readonly api = inject(DevicesApi);

  readonly id = signal('');
  readonly device = signal<DeviceDetails | null>(null);
  readonly overview = signal<DeviceOverview | null>(null);
  readonly disks = signal<DeviceDisk[]>([]);
  readonly live = signal<LiveSampleEvent | null>(null);
  readonly loading = signal(true);
  readonly failed = signal(false);

  readonly snapshot = computed(() => (this.overview()?.snapshot ?? null) as SnapshotDoc | null);
  readonly online = computed(() => this.device()?.connection === 'Online');

  async load(id: string): Promise<void> {
    this.id.set(id);
    this.loading.set(true);
    this.failed.set(false);
    this.live.set(null);
    try {
      const [device, overview, disks] = await Promise.all([
        firstValueFrom(this.api.get(id)),
        firstValueFrom(this.api.overview(id)),
        firstValueFrom(this.api.disks(id)),
      ]);
      this.device.set(device);
      this.overview.set(overview);
      this.disks.set(disks);
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }

  async reloadOverview(): Promise<void> {
    try {
      this.overview.set(await firstValueFrom(this.api.overview(this.id())));
    } catch {
      // Keep the last snapshot.
    }
  }

  async reloadDevice(): Promise<void> {
    try {
      this.device.set(await firstValueFrom(this.api.get(this.id())));
    } catch {
      // Keep the header.
    }
  }
}
