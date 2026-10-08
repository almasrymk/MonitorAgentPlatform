import { HttpClient, HttpContext, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import type { components } from './schema';
import { Paged, query } from './models';
import { SKIP_ERROR_TOAST } from './http-context';

type Schemas = components['schemas'];

export type Alert = Schemas['AlertDto'];
export type PlatformAlert = Schemas['PlatformAlertDto'];
export type MonitorPoint = Schemas['MonitorPointDto'];
export type NotificationItem = Schemas['NotificationDto'];
export type UnreadCount = Schemas['UnreadCountDto'];
export type AlertSettings = Schemas['AlertSettingsDto'];
export type Recipient = Schemas['RecipientDto'];
export type GeneralSettings = Schemas['GeneralSettingsDto'];
export type DeviceConfiguration = Schemas['DeviceConfigurationDto'];
export type ConfigDocument = Schemas['ConfigDocument'];
export type MonitorPointInput = Schemas['MonitorPointInput'];

const BASE = '/api/v1';

export interface AlertQuery {
  locationId?: string | null;
  deviceId?: string | null;
  severity?: string | null;
  status?: string | null;
  category?: string | null;
  from?: string | null;
  to?: string | null;
  sort?: string | null;
  page?: number;
  pageSize?: number;
}

export interface NotificationQuery {
  locationId?: string | null;
  severity?: string | null;
  from?: string | null;
  to?: string | null;
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class AlertsApi {
  private readonly http = inject(HttpClient);

  list(q: AlertQuery): Observable<Paged<Alert>> {
    return this.http.get<Paged<Alert>>(`${BASE}/alerts`, { params: query({ ...q }) });
  }

  get(id: string): Observable<Alert> {
    return this.http.get<Alert>(`${BASE}/alerts/${id}`);
  }

  acknowledge(id: string): Observable<void> {
    return this.http.post<void>(`${BASE}/alerts/${id}/acknowledge`, {});
  }

  resolve(id: string): Observable<void> {
    return this.http.post<void>(`${BASE}/alerts/${id}/resolve`, {});
  }

  device(deviceId: string, status = 'all', pageSize = 20): Observable<Paged<Alert>> {
    return this.http.get<Paged<Alert>>(`${BASE}/devices/${deviceId}/alerts`, { params: query({ status, pageSize }) });
  }

  /** The device's monitor points; an empty list is normal before the agent reports any. */
  monitorPoints(deviceId: string): Observable<MonitorPoint[]> {
    return this.http.get<MonitorPoint[]>(`${BASE}/devices/${deviceId}/monitor-points`, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) });
  }

  platform(q: AlertQuery & { tenantId?: string | null }): Observable<Paged<PlatformAlert>> {
    return this.http.get<Paged<PlatformAlert>>(`${BASE}/platform/alerts`, { params: query({ ...q }) });
  }
}

/** The in-app feed. `platform` = the platform feed (no workspace selected). */
@Injectable({ providedIn: 'root' })
export class NotificationsApi {
  private readonly http = inject(HttpClient);
  private readonly quiet = new HttpContext().set(SKIP_ERROR_TOAST, true);

  list(q: NotificationQuery, platform = false): Observable<Paged<NotificationItem>> {
    return this.http.get<Paged<NotificationItem>>(`${BASE}${platform ? '/platform' : ''}/notifications`, { params: query({ ...q }) });
  }

  /** Bell badge: never shows an error toast. */
  unread(platform = false): Observable<UnreadCount> {
    return this.http.get<UnreadCount>(`${BASE}${platform ? '/platform' : ''}/notifications/unread-count`, { context: this.quiet });
  }

  markRead(body: { ids?: string[]; all?: boolean }, platform = false): Observable<void> {
    return this.http.post<void>(`${BASE}${platform ? '/platform' : ''}/notifications/read`, body);
  }
}

@Injectable({ providedIn: 'root' })
export class SettingsApi {
  private readonly http = inject(HttpClient);

  general(): Observable<GeneralSettings> {
    return this.http.get<GeneralSettings>(`${BASE}/settings/general`);
  }

  updateGeneral(body: GeneralSettings): Observable<GeneralSettings> {
    return this.http.put<GeneralSettings>(`${BASE}/settings/general`, body);
  }

  alerts(): Observable<AlertSettings> {
    return this.http.get<AlertSettings>(`${BASE}/settings/alerts`);
  }

  updateAlerts(body: { emailEnabled: boolean; inAppEnabled: boolean; webhookEnabled: boolean; webhookUrl: string | null }): Observable<void> {
    return this.http.put<void>(`${BASE}/settings/alerts`, body);
  }

  recipients(): Observable<Recipient[]> {
    return this.http.get<Recipient[]>(`${BASE}/settings/alerts/recipients`);
  }

  addRecipient(body: { name: string; email: string; events: string; locationId: string | null }): Observable<Recipient> {
    return this.http.post<Recipient>(`${BASE}/settings/alerts/recipients`, body);
  }

  updateRecipient(id: string, body: { name: string; email: string; events: string; locationId: string | null; isActive: boolean }): Observable<Recipient> {
    return this.http.put<Recipient>(`${BASE}/settings/alerts/recipients/${id}`, body);
  }

  deleteRecipient(id: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/settings/alerts/recipients/${id}`);
  }
}

/** Device configuration (thresholds) and monitor point definitions (M8); the configuration version is the ETag. */
@Injectable({ providedIn: 'root' })
export class ConfigurationApi {
  private readonly http = inject(HttpClient);

  get(deviceId: string): Observable<DeviceConfiguration> {
    return this.http.get<DeviceConfiguration>(`${BASE}/devices/${deviceId}/configuration`);
  }

  /** Saves with `If-Match: "<version>"`; a newer version on the server answers 409 CONCURRENCY_CONFLICT. */
  update(deviceId: string, document: ConfigDocument, version: number): Observable<DeviceConfiguration> {
    return this.http
      .put<DeviceConfiguration>(`${BASE}/devices/${deviceId}/configuration`, document, { headers: { 'If-Match': `"${version}"` }, observe: 'response' })
      .pipe(map((r: HttpResponse<DeviceConfiguration>) => r.body as DeviceConfiguration));
  }

  addPoint(deviceId: string, point: MonitorPointInput): Observable<MonitorPoint> {
    return this.http.post<MonitorPoint>(`${BASE}/devices/${deviceId}/monitor-points`, point);
  }

  updatePoint(deviceId: string, pointId: string, point: MonitorPointInput, version: string): Observable<MonitorPoint> {
    return this.http.put<MonitorPoint>(`${BASE}/devices/${deviceId}/monitor-points/${pointId}`, point, { headers: { 'If-Match': `"${version}"` } });
  }

  deletePoint(deviceId: string, pointId: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/devices/${deviceId}/monitor-points/${pointId}`);
  }

  defaults(): Observable<ConfigDocument> {
    return this.http.get<ConfigDocument>(`${BASE}/settings/monitoring`);
  }

  updateDefaults(document: ConfigDocument): Observable<ConfigDocument> {
    return this.http.put<ConfigDocument>(`${BASE}/settings/monitoring`, document);
  }
}