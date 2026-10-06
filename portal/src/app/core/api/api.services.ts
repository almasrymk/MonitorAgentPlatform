import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  AuditRecord,
  AuthResult,
  Location,
  LocationCard,
  LocationRequest,
  Paged,
  Role,
  Tenant,
  TenantCard,
  TenantsSummary,
  UserListItem,
  UserProfile,
  query,
} from './models';
import { SKIP_AUTH_REFRESH, SKIP_ERROR_TOAST } from './http-context';

const BASE = '/api/v1';

export interface ListQuery {
  search?: string | null;
  sort?: string | null;
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class AuthApi {
  private readonly http = inject(HttpClient);
  private readonly anonymous = new HttpContext().set(SKIP_AUTH_REFRESH, true).set(SKIP_ERROR_TOAST, true);

  login(email: string, password: string): Observable<AuthResult> {
    return this.http.post<AuthResult>(`${BASE}/auth/login`, { email, password }, { context: this.anonymous });
  }

  refresh(refreshToken: string): Observable<AuthResult> {
    return this.http.post<AuthResult>(`${BASE}/auth/refresh`, { refreshToken }, { context: this.anonymous });
  }

  logout(refreshToken: string): Observable<void> {
    return this.http.post<void>(`${BASE}/auth/logout`, { refreshToken }, { context: this.anonymous });
  }

  acceptInvitation(token: string, password: string): Observable<void> {
    return this.http.post<void>(`${BASE}/auth/invitations/accept`, { token, password }, { context: this.anonymous });
  }

  me(): Observable<UserProfile> {
    return this.http.get<UserProfile>(`${BASE}/auth/me`);
  }

  setLanguage(language: string): Observable<void> {
    return this.http.put<void>(`${BASE}/auth/me/language`, { language });
  }
}

@Injectable({ providedIn: 'root' })
export class TenantsApi {
  private readonly http = inject(HttpClient);

  list(q: ListQuery & { status?: string | null; plan?: string | null }): Observable<Paged<TenantCard>> {
    return this.http.get<Paged<TenantCard>>(`${BASE}/platform/tenants`, { params: query({ ...q }) });
  }

  summary(): Observable<TenantsSummary> {
    return this.http.get<TenantsSummary>(`${BASE}/platform/tenants/summary`);
  }

  get(id: string): Observable<Tenant> {
    return this.http.get<Tenant>(`${BASE}/platform/tenants/${id}`);
  }

  suspend(id: string, reason: string): Observable<Tenant> {
    return this.http.post<Tenant>(`${BASE}/platform/tenants/${id}/suspend`, { reason });
  }

  resume(id: string): Observable<Tenant> {
    return this.http.post<Tenant>(`${BASE}/platform/tenants/${id}/resume`, {});
  }

  archive(id: string, reason: string): Observable<Tenant> {
    return this.http.post<Tenant>(`${BASE}/platform/tenants/${id}/archive`, { reason });
  }

  openWorkspace(id: string, reason: string): Observable<void> {
    return this.http.post<void>(`${BASE}/platform/tenants/${id}/workspace-sessions`, { reason });
  }

  closeWorkspace(id: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/platform/tenants/${id}/workspace-sessions/current`);
  }
}

@Injectable({ providedIn: 'root' })
export class LocationsApi {
  private readonly http = inject(HttpClient);

  list(q: ListQuery): Observable<Paged<LocationCard>> {
    return this.http.get<Paged<LocationCard>>(`${BASE}/locations`, { params: query({ ...q }) });
  }

  get(id: string): Observable<Location> {
    return this.http.get<Location>(`${BASE}/locations/${id}`);
  }

  create(body: LocationRequest): Observable<Location> {
    return this.http.post<Location>(`${BASE}/locations`, body);
  }

  update(id: string, body: LocationRequest, version: string): Observable<Location> {
    return this.http.put<Location>(`${BASE}/locations/${id}`, body, { headers: { 'If-Match': `"${version}"` } });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/locations/${id}`);
  }
}

export interface UserQuery extends ListQuery {
  role?: string | null;
  status?: string | null;
}

@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);

  list(q: UserQuery): Observable<Paged<UserListItem>> {
    return this.http.get<Paged<UserListItem>>(`${BASE}/users`, { params: query({ ...q }) });
  }

  roles(): Observable<Role[]> {
    return this.http.get<Role[]>(`${BASE}/roles`);
  }

  invite(body: { fullName: string; email: string; role: string; locationIds: string[] }): Observable<UserListItem> {
    return this.http.post<UserListItem>(`${BASE}/users`, body);
  }

  update(id: string, body: { fullName: string; role: string; locationIds: string[] }, version: string): Observable<UserListItem> {
    return this.http.put<UserListItem>(`${BASE}/users/${id}`, body, { headers: { 'If-Match': `"${version}"` } });
  }

  activate(id: string): Observable<UserListItem> {
    return this.http.post<UserListItem>(`${BASE}/users/${id}/activate`, {});
  }

  deactivate(id: string): Observable<UserListItem> {
    return this.http.post<UserListItem>(`${BASE}/users/${id}/deactivate`, {});
  }

  resendInvitation(id: string): Observable<void> {
    return this.http.post<void>(`${BASE}/users/${id}/resend-invitation`, {});
  }
}

@Injectable({ providedIn: 'root' })
export class PlatformUsersApi {
  private readonly http = inject(HttpClient);

  list(q: UserQuery): Observable<Paged<UserListItem>> {
    return this.http.get<Paged<UserListItem>>(`${BASE}/platform/users`, { params: query({ ...q }) });
  }

  create(body: { fullName: string; email: string; role: string; password: string }): Observable<UserListItem> {
    return this.http.post<UserListItem>(`${BASE}/platform/users`, body);
  }

  activate(id: string): Observable<UserListItem> {
    return this.http.post<UserListItem>(`${BASE}/platform/users/${id}/activate`, {});
  }

  deactivate(id: string): Observable<UserListItem> {
    return this.http.post<UserListItem>(`${BASE}/platform/users/${id}/deactivate`, {});
  }

  audit(q: ListQuery & { action?: string | null; tenantId?: string | null }): Observable<Paged<AuditRecord>> {
    return this.http.get<Paged<AuditRecord>>(`${BASE}/platform/audit`, { params: query({ ...q }) });
  }
}
