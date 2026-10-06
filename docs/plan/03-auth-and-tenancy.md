# 03 - Authentication, authorization and tenant isolation

## 1. User tokens

| Item | Value |
|---|---|
| Access token | JWT, HS256, 15 minutes. Key `Jwt:SigningKey` (>= 32 bytes, from the environment) |
| Issuer / audience | `monitor-cloud` / `monitor-cloud-portal` |
| Refresh token | 256-bit random, stored as HMAC-SHA256, 14 days, **rotated on every use**. Re-use of a rotated token revokes the whole family and is audited |
| Lock-out | 5 failed sign-ins lock the user for 15 minutes (`AUTH_LOCKED`, HTTP 423) |
| Password rules | 10+ characters, upper, lower, digit, symbol; not equal to the e-mail |
| Storage in the browser | Access token in memory only; refresh token in `sessionStorage` (see 07) |

Access-token claims (`MapInboundClaims = false`):

| Claim | Content |
|---|---|
| `sub` | user id |
| `email`, `name` | |
| `role` | one role |
| `typ` | `user` |
| `tid` | tenant id (absent for platform users) |
| `loc` | comma-separated location ids when the user is restricted to locations (absent = all) |
| `perm` | one claim per permission |
| `lang` | `en` or `ar` |
| `jti` | unique id |

A suspended or archived tenant's users cannot sign in or refresh (`AUTH_TENANT_SUSPENDED`).

## 2. Roles and permissions

Permissions are constants in `Domain/Identity/Permissions.cs`; the role-to-permission map is code, not data.

| Permission | Platform Admin | Platform Support | Administrator | IT Manager | Technician | Report Viewer |
|---|:-:|:-:|:-:|:-:|:-:|:-:|
| `platform.dashboard.read` | x | x | | | | |
| `platform.tenants.read` | x | x | | | | |
| `platform.tenants.manage` (create, edit, suspend, resume, archive) | x | | | | | |
| `platform.workspace.open` | x | x | | | | |
| `platform.plans.read` | x | x | | | | |
| `platform.users.manage` | x | | | | | |
| `platform.settings.manage` | x | | | | | |
| `platform.audit.read` | x | x | | | | |
| `platform.licensing.sync` | x | | | | | |
| `dashboard.read` | x | x | x | x | x | x |
| `locations.read` | x | x | x | x | x | x |
| `locations.manage` | x | | x | x | | |
| `devices.read` | x | x | x | x | x | x |
| `devices.manage` (rename, move, retire) | x | | x | x | | |
| `devices.enroll` (create enrollment codes) | x | | x | x | | |
| `monitorpoints.manage` | x | | x | x | x | |
| `devices.configure` | x | | x | x | | |
| `alerts.read` | x | x | x | x | x | x |
| `alerts.manage` (acknowledge, resolve) | x | x | x | x | x | |
| `notifications.read` | x | x | x | x | x | x |
| `reports.read` | x | x | x | x | x | x |
| `reports.generate` | x | x | x | x | | x |
| `subscription.read` | x | x | x | x | | |
| `users.manage` | x | | x | | | |
| `settings.manage` | x | | x | | | |
| `archive.read` | x | x | x | x | | |
| `archive.manage` | x | x | x | | | |
| `archive.internal` (internal notes/files, remote-access entries) | x | x | | | | |
| `audit.read` | x | x | x | | | |

Role constants: `PlatformAdmin`, `PlatformSupport`, `Administrator`, `ITManager`, `Technician`,
`ReportViewer`. Display names follow the designs ("Administrator", "IT Manager", "Technician",
"Report Viewer") and the "Permissions" column of the users table shows: `Full Access`, `Manage Devices`,
`Limited Access`, `View Reports`.

## 3. Current user and tenant scope

```csharp
public interface ICurrentUser {
    bool IsAuthenticated { get; }
    ActorType ActorType { get; }          // User | Device | System
    Guid? UserId { get; }  Guid? DeviceId { get; }
    Guid? TenantId { get; }               // the caller's own tenant; null for platform users
    string? Role { get; }  bool IsPlatform { get; }
    IReadOnlyCollection<Guid> LocationScope { get; }   // empty = unrestricted inside the tenant
    bool HasPermission(string permission);
    string? IpAddress { get; }  string? CorrelationId { get; }
}

public interface ITenantContext {
    Guid? TenantId { get; }               // the tenant filter in effect; null = all tenants
    IReadOnlyCollection<Guid> LocationScope { get; }
    bool IsUnrestricted { get; }          // true only for platform users without X-Tenant-Id and for system jobs
}
```

Resolution (`TenantContextMiddleware`, after authentication):

| Caller | `X-Tenant-Id` header | Resulting scope |
|---|---|---|
| Tenant user | ignored (a different value is rejected with `AUTH_FORBIDDEN` and audited) | own tenant, own location scope |
| Platform user | absent | unrestricted |
| Platform user | present, tenant exists | that tenant (the "workspace") |
| Device | ignored | the device's tenant |
| Background job | - | `RunAsSystem()` or `RunAsTenant(id)` explicitly |

Endpoints under `/api/v1/platform/**` require an unrestricted platform scope. All other business endpoints
require a tenant scope: a platform user calling them without `X-Tenant-Id` gets `TENANT_SCOPE_REQUIRED`.

## 4. Isolation enforcement (four layers)

1. **Query filters.** For every `ITenantOwned` entity:
   `e => ctx.IsUnrestricted || e.TenantId == ctx.TenantId`. Entities with a `LocationId` implement
   `ILocationScoped` and add `&& (ctx.LocationScope.Count == 0 || ctx.LocationScope.Contains(e.LocationId))`.
   Filters are built by reflection in `OnModelCreating`, so a new entity cannot be forgotten.
2. **Write guard.** `SaveChangesAsync` rejects any added/modified/deleted `ITenantOwned` row whose
   `TenantId` differs from the scope (`AUTH_FORBIDDEN`, "Cross-tenant write rejected").
3. **Raw SQL.** Dapper queries and bulk writers take the tenant id from `ITenantContext` through
   `ISqlConnectionFactory.TenantParameters()`. All raw SQL lives in `Infrastructure/**/Sql/*.sql` embedded
   resources; a convention test fails when a statement that touches a tenant table has no `TenantId` predicate.
4. **Tests.** The generic cross-tenant suite in 09 runs every endpoint as tenant B against tenant A's data.

`IgnoreQueryFilters()` is allowed only in: sign-in by e-mail, device token exchange, enrollment, seeding and
the licensing sync. An architecture test lists the allowed call sites.

## 5. Workspace access by platform staff ("Open Workspace")

1. `POST /api/v1/platform/tenants/{id}/workspace-sessions` with `{ "reason": "..." }` (reason required,
   10+ characters) writes an audit record `workspace.opened` and returns `204`.
2. The portal then sends `X-Tenant-Id` on every request while the user is inside the workspace and shows a
   persistent banner "Viewing <customer> as platform staff".
3. Platform Support can read and can acknowledge/resolve alerts in a workspace; it cannot manage users,
   settings or devices (see the matrix).
4. Leaving the workspace writes `workspace.closed`.

## 6. Device tokens

Separate signing key (`Jwt:DeviceSigningKey`), audience `monitor-agent-gateway`, lifetime 60 minutes.
Claims: `sub` = device id, `tid`, `typ` = `device`, `fp` = fingerprint, `jti`. The REST API rejects
`typ=device` tokens and the gateway rejects `typ=user` tokens (two JWT bearer schemes: `Bearer` and
`Device`). Details of issuing are in 05.

## 7. Audit

Written in the same transaction as the change through `IAuditLogger.Add(action, entityType, entityId,
details)`. Always audited: sign-in success/failure, refresh-token re-use, user and role changes, tenant
create/suspend/resume/archive, workspace open/close, device enroll/retire/move, enrollment-code creation,
configuration change, alert acknowledge/resolve by a user, licensing sync failures, archive internal reads,
cross-tenant attempts. Failures that roll back the transaction are written with `WriteNowAsync`.
