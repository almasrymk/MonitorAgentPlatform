# 04 - Integration 1: Licensing Platform

The Licensing Platform stays the only owner of plans, subscriptions, licences and seat counts. Monitor Cloud
reads entitlements, activates and releases seats on behalf of devices, and caches the result.

## 1. Mapping

| Licensing Platform | Monitor Cloud |
|---|---|
| Tenant (the vendor that sells Monitor Agent) | Not modelled. Identified by the API client Monitor Cloud uses |
| Product, code from `Licensing:ProductCode` (default `000001`) | The Monitor Agent product |
| Customer | `Tenant` (`Tenants.LicensingCustomerId`) |
| Subscription + Plan (code, name, features, price) | `TenantEntitlement` |
| License (product key, `maxActivations`, `activeActivations`, status, expiry) | Seats: `TenantEntitlement.MaxDevices / ActiveSeats` |
| LicenseActivation (`deviceId`) | `Device` + `DeviceLicense` (`deviceId` = `Device.Fingerprint`) |

Feature codes Monitor Cloud understands (plan features in the Licensing Platform; lower-case, dot-separated):

| Code | Enables |
|---|---|
| `monitoring` | Devices, live data, history (every plan) |
| `alerts` | Alerts and in-app notifications |
| `notifications.email` | E-mail delivery |
| `notifications.webhook` | Webhook delivery |
| `monitorpoints` | Editing monitor points from the cloud |
| `reports.basic` | Overview, device health, alerts reports |
| `reports.advanced` | Performance, network usage, custom reports, scheduled exports |
| `archive` | Customer archive |
| `remote.actions` | Remote actions (later milestone) |

Unknown codes are stored and ignored. A missing entitlement means no features.

## 2. Port and adapters

```csharp
// Application/Licensing/Contracts/ILicensingGateway.cs
public interface ILicensingGateway {
    Task<Result<SeatActivation>> ActivateSeatAsync(ActivateSeatRequest request, CancellationToken ct);
    Task<Result<SeatActivation>> RefreshSeatAsync(Guid licenseId, string deviceId, string? appVersion, string? os, CancellationToken ct);
    Task<Result> ReleaseSeatAsync(Guid licenseId, string deviceId, CancellationToken ct);
    Task<Result<CustomerEntitlements>> GetEntitlementsAsync(Guid licensingCustomerId, CancellationToken ct);
    Task<Result<PagedResult<LicensingCustomer>>> ListCustomersAsync(int page, int pageSize, DateTimeOffset? updatedSince, CancellationToken ct);
    Task<Result<IReadOnlyList<LicensingPlan>>> ListPlansAsync(CancellationToken ct);
    Task<Result<ChangeFeedPage>> GetChangesAsync(string? cursor, int take, CancellationToken ct);
    Task<Result<string>> GetSigningKeysJsonAsync(CancellationToken ct);
}

public sealed record ActivateSeatRequest(string ProductKey, string DeviceId, string DeviceName, string AppVersion, string Os, string IdempotencyKey);
public sealed record SeatActivation(Guid LicenseId, Guid CustomerId, Guid SubscriptionId, string LicenseNumber, string PlanCode,
    IReadOnlyList<string> Features, DateTimeOffset? ExpiresAt, int? MaxActivations, int ActiveActivations,
    DateTimeOffset CheckAfter, DateTimeOffset OfflineValidUntil, string Token, string Kid, bool AlreadyActivated);
```

| Adapter | When | Notes |
|---|---|---|
| `HttpLicensingGateway` | `Licensing:Mode = Live` | Typed `HttpClient`, client-credentials token cached until 1 minute before expiry, retried once on 401. Resilience: 10 s timeout, 3 retries with jitter on 5xx/timeouts (never on 4xx), circuit breaker. Maps problem `code` to `Error` unchanged |
| `FakeLicensingGateway` | `Licensing:Mode = Fake` (default in Development and tests) | In-memory, loaded from `seed/licensing-fake.json`, enforces limits and statuses with the same error codes, signs tokens with a development ES256 key |

Configuration:

```json
"Licensing": {
  "Mode": "Fake",
  "BaseUrl": "",
  "ClientId": "",
  "ClientSecret": "",
  "ProductCode": "000001",
  "SyncIntervalSeconds": 60,
  "FullReconcileHours": 24,
  "EntitlementStaleAfterMinutes": 30,
  "UnlicensedGraceDays": 14,
  "FailOpenHours": 72
}
```

`ClientSecret` is read from the environment in every non-development environment. The API client needs the
scopes `licenses.activate licenses.validate licenses.read customers.read subscriptions.read catalog.read`.

## 3. Changes required in the Licensing Platform (work package LP)

Do these in `almasrymk/LicensingPlatform`, following that repository's conventions (application services,
`Result`, `[RequireScope]`, FluentValidation, its test style). Each item needs unit/API tests there.

| Id | Change |
|---|---|
| LP-1 | Add scopes `customers.read`, `subscriptions.read`, `catalog.read` to `ApiScopes.All`. |
| LP-2 | `LicenseCheckResponse`: add `licenseId`, `customerId`, `subscriptionId`. Signed token: add claim `cid` (customer id). Existing fields and claims unchanged. |
| LP-3 | New `IntegrationController`, route `api/v1/integration`, client tokens only, rate-limited with the existing `Licensing` policy. Endpoints below. |
| LP-4 | Change feed over the outbox: integration events exposed in order with a cursor. Requires `CustomerId` on the outbox row (new nullable column, filled for customer-owned aggregates). |
| LP-5 | Seat operations by licence id (no product key needed after activation). |
| LP-6 | (Later, optional) Webhook delivery signed with HMAC-SHA256. The secret must be stored recoverably (Data Protection), not hashed as today. Monitor Cloud works without it. |

LP-3 / LP-4 / LP-5 endpoints (all scoped to the caller's tenant by the token):

| Method and path | Scope | Returns |
|---|---|---|
| `GET /api/v1/integration/customers?page&pageSize&updatedSince` | `customers.read` | `PagedResult<{ id, name, email, phone, country, status, createdAt }>` |
| `GET /api/v1/integration/customers/{id}/entitlements?productCode` | `subscriptions.read` | `{ customerId, customerStatus, subscriptions: [{ id, status, planCode, planName, planVersion, features[], startDate, endDate, trialEndsAt }], licenses: [{ id, licenseNumber, subscriptionId, status, expiresAt, maxActivations, activeActivations, features[] }] }` (only the given product) |
| `GET /api/v1/integration/plans?productCode` | `catalog.read` | published plans: `{ id, code, name, version, features[], maxActivations, durationDays, price: { amount, currency } }` |
| `GET /api/v1/integration/licenses/{id}/activations` | `licenses.read` | `[{ deviceId, deviceName, status, activatedAt, lastSeenAt, appVersion, os }]` |
| `POST /api/v1/integration/licenses/{id}/devices/{deviceId}/heartbeat` | `licenses.validate` | `LicenseCheckResponse` (same rules as `heartbeat`, fresh token) |
| `POST /api/v1/integration/licenses/{id}/devices/{deviceId}/release` | `licenses.activate` | `204`; `LIC_DEVICE_NOT_ACTIVATED` if not active |
| `GET /api/v1/integration/changes?cursor&take` | `subscriptions.read` | `{ items: [{ id, type, occurredAt, customerId, subscriptionId, licenseId }], nextCursor, hasMore }` ordered by `(occurredAt, id)`; types are the existing `*V1` event names |

Until LP is deployed, Monitor Cloud runs in `Fake` mode. `ContractTests` (09) prove that the fake and the
live platform answer the same.

## 4. Flows

### 4.1 Enrollment (seat activation)

```
Agent                    Monitor Cloud                               Licensing Platform
  | POST /api/agent/v1/enroll                                              |
  | { productKey, fingerprint, hostname, os, agentVersion, locationCode? } |
  |----------------------->|                                               |
  |                        | POST /api/v1/licensing/activate               |
  |                        | Idempotency-Key = new GUID per enrollment request
  |                        |---------------------------------------------->|
  |                        |<-- 200 { licenseId, customerId, token, ... } -|
  |                        | find Tenant by LicensingCustomerId            |
  |                        |   not found -> auto-provision (4.4)           |
  |                        | create/reuse Device, credential, DeviceLicense|
  |<-- 200 { deviceId, deviceSecret, gatewayUrl, licenseToken, signingKeys }
```

Rules:

- The `Idempotency-Key` is created once per enrollment request and reused only by the HTTP retries of that
  request. Never derive it from the key or the fingerprint: a later re-enrollment must reach the Licensing
  Platform again.
- A failed activation returns the Licensing error code unchanged (`LIC_ACTIVATION_LIMIT_REACHED`, ...),
  records an `EnrollmentAttempt` and, when the tenant can be identified, creates a notification for its
  administrators.
- Same fingerprint, same tenant, device `Active`: enrollment is idempotent; a **new** secret is issued and
  the old one revoked (covers reinstall).
- Same fingerprint, device `Retired`: the device is reactivated in its previous location.
- The product key is never stored or logged by Monitor Cloud; only its 6-character prefix is kept in the
  attempt record.
- Tenant `Suspended` or `Archived`: `ENROLL_TENANT_NOT_ACTIVE`; the seat just activated is released.

### 4.2 Licence refresh

`LicenseRefreshJob` runs every minute, picks `DeviceLicenses` with `CheckAfter <= now` (batch 100, at most
200 calls per minute to stay under the Licensing rate limit), calls `RefreshSeatAsync`, stores the new token
and pushes it to the device over the stream (`LicenseUpdate`). On a `LIC_*` rejection the device becomes
`Unlicensed` with that reason code, a `license` alert is raised on it, and the gateway tells the agent.
Network failures are retried on the next run and never change the state.

### 4.3 Entitlement sync

`LicensingSyncJob`:

1. Every `SyncIntervalSeconds`: read `changes` from the stored cursor; for each distinct `customerId` call
   `GetEntitlementsAsync` and upsert `TenantEntitlement`; advance the cursor in the same transaction.
2. Every `FullReconcileHours`: page through all customers and reconcile every linked tenant.
3. `EntitlementChangedV1` is raised only when plan, status, features, limit or dates actually change.

Derivation of `TenantEntitlement` from the Licensing answer:

| Field | Rule |
|---|---|
| Subscription used | the one with status `Active` or `Trial` and the latest `endDate`; if none, the most recently ended one |
| `SubscriptionStatus` | that subscription's status, or `None` |
| `PlanCode`, `PlanName`, `Features` | from that subscription |
| `MaxDevices` | sum of `maxActivations` of licences with status `Active`; NULL if any is unlimited |
| `ActiveSeats` | sum of `activeActivations` of those licences |
| `RenewsAt` | that subscription's `endDate` |

### 4.4 Auto-provisioning a tenant

When an activation succeeds for a `customerId` with no tenant: create `Tenant` (name, country from
`GET integration/customers/{id}`; code = slug of the name, made unique), its default location, and its
entitlement. No user is created; the platform admin invites the first Administrator. A platform
notification "New customer provisioned" is created.

### 4.5 Behaviour when things go wrong

| Situation | Behaviour |
|---|---|
| Licensing unreachable during enrollment | `503 LICENSING_UNAVAILABLE`; the agent retries with back-off |
| Sync failing | Entitlements keep their last value; `SyncState.ConsecutiveFailures` grows; a platform alert after 5 failures; `/health/ready` degraded |
| Entitlement older than `EntitlementStaleAfterMinutes` | Shown with a "last synced" warning in the subscription screen |
| No successful sync for `FailOpenHours` | Features stay as cached (fail open); a critical platform notification is raised. The platform never locks customers out because Licensing is down |
| Subscription `Expired` | Portal becomes read-only for the tenant (`ENTITLEMENT_EXPIRED` on commands), ingestion continues |
| Subscription `Suspended` / `Cancelled` | Tenant users see the subscription screen only; devices keep heartbeating; telemetry is dropped at the gateway |
| Device `Unlicensed` | Decision D19: for `Licensing:UnlicensedGraceDays` (14) it keeps reporting status, minute metrics and alerts; history queries are limited to 24 hours; monitor-point editing, configuration, reports and remote actions return `DEVICE_UNLICENSED`. After the grace period the gateway drops everything except heartbeats and health becomes `Unknown`. The UI shows the reason code |

## 5. Screens fed by this integration

- **Subscription & Licenses** (customer): current plan, status, renewal date, devices used / limit, licence
  usage by operating system, All / Licensed / Unlicensed device tabs.
- **Plans** (platform): read-only list from `ListPlansAsync` with the number of customers per plan. No
  editing; a link opens the Licensing Platform.
- **Platform dashboard**: licensed/unlicensed device counts, subscription distribution by plan, expiring
  subscriptions (`RenewsAt` within 30 days).
- **Customers**: plan name, licence usage bar, next renewal, status pill (`Active`, `Expiring Soon` when
  `RenewsAt` is within 30 days, `Suspended`).

## 6. Development seeding against a real Licensing Platform

`tools/MonitorCloud.LicensingSeeder` (console) signs in to a local Licensing Platform as its platform admin,
creates the vendor tenant, the product, the four plans, the sample customers, their subscriptions and
licences from `seed/customers.json`, creates an API client with the scopes of section 2, and writes
`seed/licensing-live.local.json` (git-ignored) containing the client credentials and the product keys so
`MonitorCloud.DeviceSimulator` can enroll devices in `Live` mode. It is idempotent (looks records up by code
or name before creating them).
