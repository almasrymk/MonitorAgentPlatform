# 06 - REST API and SignalR

Base path `/api/v1`. JSON, camelCase, enums as strings, timestamps ISO-8601 UTC. Errors are
`application/problem+json` with `code` and `traceId`. Lists return
`{ "items": [...], "total": n, "page": 1, "pageSize": 20 }`; `pageSize` max 200.

Scope column: **P** = platform scope only (`/platform/**`), **T** = tenant scope (the caller's tenant, or the
workspace selected with `X-Tenant-Id` by a platform user). Each row names the permission it requires.

## 1. Auth

| Method | Path | Notes |
|---|---|---|
| POST | `/auth/login` | `{ email, password }` -> `{ accessToken, expiresAt, refreshToken, user: { id, fullName, email, role, tenantId, tenantName, permissions[], language } }` |
| POST | `/auth/refresh` | `{ refreshToken }` -> new pair |
| POST | `/auth/logout` | `{ refreshToken }` -> 204 |
| GET | `/auth/me` | profile |
| PUT | `/auth/me/password` | `{ currentPassword, newPassword }` |
| PUT | `/auth/me/language` | `{ language }` |
| POST | `/auth/invitations/accept` | `{ token, password }` (invited users set their password) |

## 2. Platform (P)

| Method | Path | Permission | Returns / does |
|---|---|---|---|
| GET | `/platform/dashboard?trendDays=7&severityDays=30` | `platform.dashboard.read` | Composite for the Platform Admin Dashboard (see 07 section 5.1): KPI tiles with 30-day deltas, incident trend, incidents by severity, subscription distribution, top customers, expiring subscriptions, recent alerts, device health, devices by OS, recent activity |
| GET | `/platform/tenants?search&plan&health&subscriptionStatus&sort&page&pageSize` | `platform.tenants.read` | Customer cards: name, plan, status pill, locations, devices, healthy/warning/critical counts, health score, licence usage, next renewal |
| GET | `/platform/tenants/summary` | `platform.tenants.read` | Tiles: total, active, expiring soon, suspended, with deltas |
| POST | `/platform/tenants` | `platform.tenants.manage` | `{ name, code?, country, city, timeZone, licensingCustomerId? }` |
| GET / PUT | `/platform/tenants/{id}` | read / manage | |
| POST | `/platform/tenants/{id}/suspend` | manage | `{ reason }` |
| POST | `/platform/tenants/{id}/resume` | manage | |
| POST | `/platform/tenants/{id}/archive` | manage | `{ reason }` |
| POST | `/platform/tenants/{id}/workspace-sessions` | `platform.workspace.open` | `{ reason }` -> 204 (audit) |
| DELETE | `/platform/tenants/{id}/workspace-sessions/current` | `platform.workspace.open` | 204 (audit) |
| GET | `/platform/plans` | `platform.plans.read` | Plans from Licensing with customer counts (read-only) |
| GET | `/platform/notifications?severity&page` | `platform.dashboard.read` | Platform-wide feed |
| GET | `/platform/alerts?severity&tenantId&from&to&page` | `platform.dashboard.read` | Alerts across customers |
| GET / POST | `/platform/users` | `platform.users.manage` | Platform users |
| PUT | `/platform/users/{id}` ; POST `/{id}/activate`, `/{id}/deactivate`, `/{id}/reset-password` | `platform.users.manage` | |
| GET | `/platform/audit?tenantId&actor&action&from&to&page` | `platform.audit.read` | |
| GET / PUT | `/platform/settings` | `platform.settings.manage` | Branding name, offline-alert delay, retention, e-mail sender |
| GET | `/platform/licensing/status` | `platform.dashboard.read` | Mode, last sync, failures |
| POST | `/platform/licensing/sync` | `platform.licensing.sync` | Run a full reconcile now |
| GET | `/platform/reports/{type}?from&to` | `platform.dashboard.read` | Cross-customer reports |

## 3. Customer area (T)

### Dashboard and locations

| Method | Path | Permission | Returns / does |
|---|---|---|---|
| GET | `/dashboard?trendDays=7` | `dashboard.read` | Composite for Customer Dashboard / Customer Workspace: tiles (locations, devices, online, healthy, warning, critical, licensed, unlicensed) with deltas, locations overview cards, locations health, incident trend, top problematic devices, device status by location, recent alerts, licence summary |
| GET | `/locations` | `locations.read` | Location cards with device counts and health score |
| POST | `/locations` | `locations.manage` | |
| GET / PUT | `/locations/{id}` | read / manage | |
| DELETE | `/locations/{id}` | manage | Only when it has no devices and is not the default (`LOCATION_NOT_EMPTY`) |
| GET | `/locations/{id}/dashboard?trendDays=7` | `dashboard.read` | Composite for Location Overview: tiles, incident trend, devices by OS, device health, resource averages (CPU, RAM, disk, health score across online devices), top problematic devices, recent alerts, location summary (address, contact, last sync) |
| POST | `/locations/{id}/enrollment-codes` | `devices.enroll` | `{ expiresInHours, maxUses? }` -> `{ code, expiresAt, installCommands: { windows, linux, macos } }` (code shown once) |
| GET | `/locations/{id}/enrollment-codes` ; DELETE `/{codeId}` | `devices.enroll` | List (prefix only) / revoke |

### Devices

| Method | Path | Permission | Returns / does |
|---|---|---|---|
| GET | `/devices?locationId&search&os&status&license&sort&page&pageSize` | `devices.read` | `status` in `online, offline, healthy, warning, critical`; `license` in `licensed, unlicensed`; `sort` in `severity` (default), `name`, `lastSeen`, `cpu`. Item: id, name, osFamily, osName, localIp, connection, health, licenseState, cpu, ram, disk, lastSeenAt, uptimeSeconds, openAlerts |
| GET | `/devices/summary?locationId` | `devices.read` | Tiles: total, online, offline, licensed, warning, critical with deltas |
| GET | `/devices/{id}` | `devices.read` | Header: name, customer, location, OS, IP, last seen, uptime, agent version, connection, health, licence, applied/target config version |
| PUT | `/devices/{id}` | `devices.manage` | `{ name, locationId }` |
| POST | `/devices/{id}/retire` | `devices.manage` | Releases the seat |
| POST | `/devices/{id}/unlicense` | `devices.manage` | Releases the seat, keeps the device (`Unlicensed`) |
| GET | `/devices/{id}/overview` | `devices.read` | Latest snapshot (05 section 5) + monitor point shortcuts + last 10 messages/issues |
| GET | `/devices/{id}/metrics?from&to&metrics=cpu,ram,disk,network` | `devices.read` | Series; resolution chosen by the server: <= 6 h minutes, otherwise hours; max 1,500 points per series |
| GET | `/devices/{id}/disks` | `devices.read` | Partitions with usage and status |
| GET | `/devices/{id}/inventory/{kind}` | `devices.read` | `hardware, os, network, disks, programs, services, users, sensors` |
| GET | `/devices/{id}/monitor-points` | `devices.read` | Definitions + state |
| POST / PUT / DELETE | `/devices/{id}/monitor-points[/{pointId}]` | `monitorpoints.manage` | Changes the configuration document and bumps its version. Feature `monitorpoints` |
| GET / PUT | `/devices/{id}/configuration` | read: `devices.read`; write: `devices.configure` | Thresholds, intervals; `If-Match` with the version |
| GET | `/devices/{id}/alerts?status&page` | `alerts.read` | |
| POST | `/devices/{id}/live-sessions` | `devices.read` | 204; keeps live mode on for 60 s |
| POST | `/devices/{id}/commands` | `devices.manage` | Milestone M11 |

### Alerts and notifications

| Method | Path | Permission | Notes |
|---|---|---|---|
| GET | `/alerts?locationId&deviceId&severity&status&category&from&to&page` | `alerts.read` | Default `status=open`, newest first |
| GET | `/alerts/{id}` | `alerts.read` | |
| POST | `/alerts/{id}/acknowledge` | `alerts.manage` | |
| POST | `/alerts/{id}/resolve` | `alerts.manage` | Not allowed for `device-offline` and `license` (they resolve themselves) |
| GET | `/notifications?locationId&severity&from&to&page` | `notifications.read` | In-app feed (Location Notifications screen) |
| GET | `/notifications/unread-count` | `notifications.read` | Bell badge |
| POST | `/notifications/read` | `notifications.read` | `{ ids[] }` or `{ all: true }` |

### Subscription, users, settings

| Method | Path | Permission | Notes |
|---|---|---|---|
| GET | `/subscription` | `subscription.read` | Plan, status, renewal date, devices used/limit, licensed/unlicensed counts, usage by OS, last synced |
| GET | `/users?role&status&search&page` | `users.manage` | |
| POST | `/users` | `users.manage` | `{ fullName, email, role, locationIds[] }` -> invitation e-mail |
| PUT | `/users/{id}` ; POST `/{id}/activate`, `/{id}/deactivate`, `/{id}/resend-invitation` | `users.manage` | |
| GET | `/roles` | `users.manage` | The four roles with their permission summary |
| GET / PUT | `/settings/general` | `settings.manage` | Time zone, language default, offline-alert severity and delay |
| GET / PUT | `/settings/alerts` | `settings.manage` | Channel switches (e-mail, SMS, in-app, webhook) |
| GET / POST / PUT / DELETE | `/settings/alerts/recipients[/{id}]` | `settings.manage` | |
| GET / PUT | `/settings/monitoring` | `settings.manage` | Tenant default thresholds for new devices |

### Reports and archive

| Method | Path | Permission | Notes |
|---|---|---|---|
| GET | `/reports/types` | `reports.read` | The eight types with required features |
| POST | `/reports` | `reports.generate` | `{ type, locationIds[], deviceIds[], from, to, groupBy, format }` -> 202 with id; generated by a background job |
| GET | `/reports?page` | `reports.read` | Recent reports |
| GET | `/reports/{id}/download` | `reports.read` | File stream |
| GET / PUT | `/archive/profile` | `archive.read` / `archive.manage` | Company details |
| GET / POST / PUT / DELETE | `/archive/contacts[/{id}]` | read / manage | |
| GET / POST / DELETE | `/archive/notes[/{id}]` | read / manage | Internal notes need `archive.internal` |
| GET / POST / DELETE | `/archive/files[/{id}]` ; GET `/archive/files/{id}/download` | read / manage | Multipart upload, 10 MB |
| GET / POST / PUT / DELETE | `/archive/remote-access[/{id}]` | `archive.internal` | Values masked in lists; `GET /{id}/reveal` returns them and is audited |
| GET | `/audit?from&to&actor&action&page` | `audit.read` | Tenant audit |

## 4. Agent (outside `/api/v1`)

`POST /api/agent/v1/enroll`, `POST /api/agent/v1/token`, `POST /api/agent/v1/credential/rotate` and the gRPC
service - see 05.

## 5. SignalR hub `/hubs/live`

Authentication: the access token (`access_token` query parameter during negotiation, as SignalR requires).
The hub adds the connection to groups on request and checks every subscription against the caller's scope.

| Client -> server | Effect |
|---|---|
| `SubscribeTenant()` | joins `tenant:{tenantId}` (platform users: the workspace tenant) |
| `SubscribePlatform()` | joins `platform` (platform scope only) |
| `SubscribeLocation(locationId)` / `Unsubscribe...` | joins `location:{id}` |
| `SubscribeDevice(deviceId)` / `Unsubscribe...` | joins `device:{id}` |

| Server -> client | Group | Payload |
|---|---|---|
| `deviceStateChanged` | tenant, location, device | `{ deviceId, connection, health, licenseState, cpu, ram, disk, lastSeenAt }` |
| `summaryChanged` | platform, tenant, location | `{ scope, id }` - a hint to refetch the composite (at most once per 2 s per group) |
| `alertRaised` / `alertUpdated` / `alertResolved` | tenant, location, device | alert list item |
| `notificationCreated` | tenant or platform | notification item |
| `liveSample` | device | `{ at, cpu, ram, diskActive, rxBps, txBps, cpuTempC }` |
| `snapshotUpdated` | device | `{ deviceId, capturedAt }` - hint to refetch `/overview` |
| `configApplied` | device | `{ deviceId, version, success, error }` |

## 6. Error codes

Stable, upper snake case, `AREA_REASON`. The portal translates by code. Add new codes to
`Application/Common/ErrorCodes.cs` and to both dictionaries in the same change.

| Code | HTTP | Meaning |
|---|---|---|
| `VALIDATION_FAILED` | 400 | Field errors in `errors` |
| `AUTH_UNAUTHORIZED` / `AUTH_FORBIDDEN` | 401 / 403 | |
| `AUTH_INVALID_CREDENTIALS` | 401 | Same answer for unknown e-mail and wrong password |
| `AUTH_LOCKED` | 423 | `retryAfterSeconds` in the body |
| `AUTH_TENANT_SUSPENDED` | 403 | |
| `AUTH_REFRESH_INVALID` | 401 | Unknown, expired, revoked or re-used refresh token |
| `TENANT_SCOPE_REQUIRED` | 400 | Platform user called a tenant endpoint without `X-Tenant-Id` |
| `TENANT_NOT_FOUND`, `LOCATION_NOT_FOUND`, `DEVICE_NOT_FOUND`, `USER_NOT_FOUND`, `ALERT_NOT_FOUND`, `REPORT_NOT_FOUND` | 404 | Also returned for rows of another tenant |
| `TENANT_INVALID_TRANSITION`, `LOCATION_NOT_EMPTY`, `LOCATION_IS_DEFAULT`, `LOCATION_CODE_TAKEN`, `USER_EMAIL_TAKEN`, `USER_LAST_ADMIN`, `ALERT_SELF_RESOLVING` | 409 | |
| `CONCURRENCY_CONFLICT` | 409 | Row version or `If-Match` mismatch |
| `FEATURE_NOT_ENTITLED` | 403 | The plan lacks the feature; `feature` in the body |
| `ENTITLEMENT_EXPIRED` | 403 | Subscription expired: commands are refused |
| `DEVICE_UNLICENSED` | 403 | Action locked for an unlicensed device |
| `DEVICE_INVALID_CREDENTIAL` | 401 | Agent token exchange |
| `ENROLL_INVALID_LOCATION_CODE`, `ENROLL_TENANT_NOT_ACTIVE`, `ENROLL_PROTOCOL_UNSUPPORTED` | 400 / 403 / 400 | Enrollment |
| `LIC_*` | as sent by Licensing | Passed through unchanged |
| `LICENSING_UNAVAILABLE` | 503 | |
| `RATE_LIMITED` | 429 | `Retry-After` header |
| `UPLOAD_TOO_LARGE`, `UPLOAD_TYPE_NOT_ALLOWED` | 413 / 415 | |

## 7. Conventions enforced by tests

- Every controller action has `[ProducesResponseType]` for its success and error shapes.
- No endpoint accepts `tenantId` in a body or query outside `/platform/**`.
- Every list endpoint has a default sort and rejects unknown `sort` values with `VALIDATION_FAILED`.
- `ETag` / `If-Match` on configuration and on entities with `RowVersion`; mismatch -> `409 CONCURRENCY_CONFLICT`.
