import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import type { components } from './schema';

export type Subscription = components['schemas']['SubscriptionDto'];
export type Plan = components['schemas']['PlanDto'];
export type LicensingStatus = components['schemas']['LicensingStatusDto'];
export type SyncOutcome = components['schemas']['SyncOutcome'];

@Injectable({ providedIn: 'root' })
export class LicensingApi {
  private readonly http = inject(HttpClient);

  subscription(): Observable<Subscription> {
    return this.http.get<Subscription>('/api/v1/subscription');
  }

  plans(): Observable<Plan[]> {
    return this.http.get<Plan[]>('/api/v1/platform/plans');
  }

  status(): Observable<LicensingStatus> {
    return this.http.get<LicensingStatus>('/api/v1/platform/licensing/status');
  }

  sync(): Observable<SyncOutcome> {
    return this.http.post<SyncOutcome>('/api/v1/platform/licensing/sync', {});
  }
}
