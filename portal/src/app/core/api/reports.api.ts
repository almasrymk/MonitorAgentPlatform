import { HttpClient, HttpContext, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

import type { components } from './schema';
import { AuditRecord, Paged, query } from './models';
import { SKIP_ERROR_TOAST } from './http-context';

type Schemas = components['schemas'];

export type ReportType = Schemas['ReportTypeDto'];
export type ReportTypes = Schemas['ReportTypesDto'];
export type Report = Schemas['ReportDto'];
export type ReportData = Schemas['ReportData'];
export type CustomerProfile = Schemas['CustomerProfileDto'];
export type ArchiveContact = Schemas['ArchiveContactDto'];
export type ContactInput = Schemas['ContactInput'];
export type ArchiveNote = Schemas['ArchiveNoteDto'];
export type ArchiveFile = Schemas['ArchiveFileDto'];
export type RemoteAccess = Schemas['RemoteAccessDto'];
export type RemoteAccessInput = Schemas['RemoteAccessInput'];
export type RemoteAccessSecret = Schemas['RemoteAccessSecretDto'];
export type Integrations = Schemas['IntegrationsDto'];
export type WebhookTest = Schemas['WebhookTestDto'];
export type PlatformSettings = Schemas['PlatformSettingsDto'];

export interface ReportRequest {
  type: string;
  locationIds?: string[];
  deviceIds?: string[];
  from?: string | null;
  to?: string | null;
  groupBy?: string | null;
  format?: string | null;
}

/** A downloaded file: its name from Content-Disposition and the bytes. */
export interface DownloadedFile {
  fileName: string;
  blob: Blob;
}

const BASE = '/api/v1';

function fileName(response: HttpResponse<Blob>, fallback: string): string {
  const header = response.headers.get('Content-Disposition') ?? '';
  const star = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (star) {
    return decodeURIComponent(star[1]);
  }
  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain ? plain[1] : fallback;
}

/** Saves a downloaded file through a temporary object URL. */
export function saveFile(file: DownloadedFile): void {
  const url = URL.createObjectURL(file.blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = file.fileName;
  link.rel = 'noopener';
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

@Injectable({ providedIn: 'root' })
export class ReportsApi {
  private readonly http = inject(HttpClient);

  types(): Observable<ReportTypes> {
    return this.http.get<ReportTypes>(`${BASE}/reports/types`);
  }

  request(body: ReportRequest): Observable<Report> {
    return this.http.post<Report>(`${BASE}/reports`, body);
  }

  list(page = 1, pageSize = 20): Observable<Paged<Report>> {
    return this.http.get<Paged<Report>>(`${BASE}/reports`, { params: query({ page, pageSize }) });
  }

  download(id: string): Observable<DownloadedFile> {
    return this.http
      .get(`${BASE}/reports/${id}/download`, { responseType: 'blob', observe: 'response' })
      .pipe(map((r) => ({ fileName: fileName(r, 'report'), blob: r.body ?? new Blob() })));
  }

  platform(type: string, from: string, to: string): Observable<ReportData> {
    return this.http.get<ReportData>(`${BASE}/platform/reports/${type}`, { params: query({ from, to }) });
  }
}

@Injectable({ providedIn: 'root' })
export class ArchiveApi {
  private readonly http = inject(HttpClient);

  profile(): Observable<CustomerProfile> {
    return this.http.get<CustomerProfile>(`${BASE}/archive/profile`);
  }

  updateProfile(body: { industry: string | null; website: string | null; phone: string | null; address: string | null; accountManager: string | null }): Observable<void> {
    return this.http.put<void>(`${BASE}/archive/profile`, body);
  }

  contacts(): Observable<ArchiveContact[]> {
    return this.http.get<ArchiveContact[]>(`${BASE}/archive/contacts`);
  }

  saveContact(id: string | null, body: ContactInput): Observable<ArchiveContact> {
    return id ? this.http.put<ArchiveContact>(`${BASE}/archive/contacts/${id}`, body) : this.http.post<ArchiveContact>(`${BASE}/archive/contacts`, body);
  }

  deleteContact(id: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/archive/contacts/${id}`);
  }

  notes(): Observable<ArchiveNote[]> {
    return this.http.get<ArchiveNote[]>(`${BASE}/archive/notes`);
  }

  addNote(body: string, isInternal: boolean): Observable<ArchiveNote> {
    return this.http.post<ArchiveNote>(`${BASE}/archive/notes`, { body, isInternal });
  }

  deleteNote(id: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/archive/notes/${id}`);
  }

  /** The screen shows its own state (also "not in your plan"), so no error toast. */
  files(): Observable<ArchiveFile[]> {
    return this.http.get<ArchiveFile[]>(`${BASE}/archive/files`, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) });
  }

  upload(file: File, displayName: string | null, isInternal: boolean): Observable<ArchiveFile> {
    const form = new FormData();
    form.append('file', file, file.name);
    if (displayName) {
      form.append('displayName', displayName);
    }
    form.append('isInternal', String(isInternal));
    return this.http.post<ArchiveFile>(`${BASE}/archive/files`, form);
  }

  download(id: string): Observable<DownloadedFile> {
    return this.http
      .get(`${BASE}/archive/files/${id}/download`, { responseType: 'blob', observe: 'response' })
      .pipe(map((r) => ({ fileName: fileName(r, 'file'), blob: r.body ?? new Blob() })));
  }

  deleteFile(id: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/archive/files/${id}`);
  }

  remoteAccess(): Observable<RemoteAccess[]> {
    return this.http.get<RemoteAccess[]>(`${BASE}/archive/remote-access`);
  }

  saveRemoteAccess(id: string | null, body: RemoteAccessInput): Observable<RemoteAccess> {
    return id ? this.http.put<RemoteAccess>(`${BASE}/archive/remote-access/${id}`, body) : this.http.post<RemoteAccess>(`${BASE}/archive/remote-access`, body);
  }

  reveal(id: string): Observable<RemoteAccessSecret> {
    return this.http.get<RemoteAccessSecret>(`${BASE}/archive/remote-access/${id}/reveal`);
  }

  deleteRemoteAccess(id: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/archive/remote-access/${id}`);
  }
}

@Injectable({ providedIn: 'root' })
export class IntegrationsApi {
  private readonly http = inject(HttpClient);

  get(): Observable<Integrations> {
    return this.http.get<Integrations>(`${BASE}/settings/integrations`);
  }

  /** `secret`: null keeps the stored one, '' removes it. */
  update(body: { webhookEnabled: boolean; webhookUrl: string | null; secret: string | null }): Observable<Integrations> {
    return this.http.put<Integrations>(`${BASE}/settings/integrations`, body);
  }

  /** A failed delivery is a 200 with success = false, shown inline. */
  test(): Observable<WebhookTest> {
    return this.http.post<WebhookTest>(`${BASE}/settings/integrations/webhook/test`, {});
  }
}

@Injectable({ providedIn: 'root' })
export class PlatformSettingsApi {
  private readonly http = inject(HttpClient);

  get(): Observable<PlatformSettings> {
    return this.http.get<PlatformSettings>(`${BASE}/platform/settings`);
  }

  update(body: PlatformSettings): Observable<PlatformSettings> {
    return this.http.put<PlatformSettings>(`${BASE}/platform/settings`, body);
  }
}

export interface AuditQuery {
  actor?: string | null;
  action?: string | null;
  tenantId?: string | null;
  from?: string | null;
  to?: string | null;
  page?: number;
  pageSize?: number;
}

/** The tenant audit (<c>/audit</c>) or, for platform staff outside a workspace, the platform audit. */
@Injectable({ providedIn: 'root' })
export class AuditApi {
  private readonly http = inject(HttpClient);

  tenant(q: AuditQuery): Observable<Paged<AuditRecord>> {
    return this.http.get<Paged<AuditRecord>>(`${BASE}/audit`, { params: query({ ...q }) });
  }

  platform(q: AuditQuery): Observable<Paged<AuditRecord>> {
    return this.http.get<Paged<AuditRecord>>(`${BASE}/platform/audit`, { params: query({ ...q }) });
  }
}
