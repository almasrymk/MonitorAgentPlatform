import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { DashboardsApi } from '../../core/api/api.services';
import { LocationDashboard } from '../../core/api/models';

/** The location being viewed, shared by the location page and its tabs. */
@Injectable()
export class LocationContext {
  private readonly api = inject(DashboardsApi);

  readonly id = signal('');
  readonly dashboard = signal<LocationDashboard | null>(null);
  readonly loading = signal(true);
  readonly failed = signal(false);

  async load(id: string): Promise<void> {
    this.id.set(id);
    this.loading.set(true);
    this.failed.set(false);
    try {
      this.dashboard.set(await firstValueFrom(this.api.location(id)));
    } catch {
      this.failed.set(true);
    } finally {
      this.loading.set(false);
    }
  }
}
