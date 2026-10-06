# 00 - Overview, decisions and glossary

## 1. What is being built

**Monitor Cloud** (shown to users as "Monitor Agent Platform") is the central web platform for the Monitor
Agent product. It has two integrations and two audiences.

```
                 Licensing Platform (existing, separate repo)
                 plans / subscriptions / licenses / device seats
                              ^
                              | REST, API-client token            (Integration 1)
                              v
+---------------------------------------------------------------+
|                         MONITOR CLOUD                         |
|  Angular portal:  Platform Admin area  |  Customer area       |
|  .NET API:  REST + SignalR  |  Agent Gateway (gRPC)           |
|  SQL Server                                                   |
+---------------------------------------------------------------+
                              ^
                              | outbound gRPC stream from device  (Integration 2)
                              |
        Monitor Agent service on customer devices (existing repo)
```

- **Platform level**: the vendor's staff see every customer, every device, subscriptions and alerts.
- **Customer level**: each customer sees only its own locations, devices, alerts, users and reports.

## 2. Repositories

| Repo | Role | Work in this plan |
|---|---|---|
| `MonitorCloud` (new) | The platform: backend, portal, simulator | Everything prefixed **MC-** |
| `almasrymk/LicensingPlatform` | Commercial entitlements | Small additions prefixed **LP-** (see 04) |
| `almasrymk/UBGMonitor` | The agent (Windows/Linux/macOS service + Avalonia desktop) | Cloud connector prefixed **AG-** (see 05) |

Each repo keeps its own conventions. In `LicensingPlatform` follow its ADRs (it deliberately uses application
services, not MediatR). In `UBGMonitor` keep the existing service structure.

## 3. Documents

| File | Content |
|---|---|
| `01-architecture.md` | Solution layout, layering rules, CQRS conventions, cross-cutting concerns, performance |
| `02-domain-and-data.md` | Modules, aggregates, tables, indexes |
| `03-auth-and-tenancy.md` | JWT, roles, permissions, tenant isolation, workspace access |
| `04-licensing-integration.md` | Integration 1: contract with the Licensing Platform and the LP- changes |
| `05-device-integration.md` | Integration 2: enrollment, device tokens, gRPC protocol, gateway, agent changes |
| `06-api.md` | REST endpoints and SignalR events |
| `07-frontend-and-design.md` | Angular structure, design tokens, screen-by-screen specification |
| `08-seed-data.md` | Default admin, sample customers, devices and history |
| `09-testing.md` | Test strategy, required tests, coverage gates, CI |
| `10-milestones.md` | Ordered work with acceptance criteria |
| `../design/*.png` | The approved screens |
| `../../proto/monitor/agent/v1/agent.proto` | The device protocol |

## 4. Decisions

"Owner" decisions were stated by the product owner. "Plan" decisions were chosen while writing this plan to
remove ambiguity; change one only through an ADR.

| # | Decision | Source |
|---|---|---|
| D1 | Backend .NET 10 Web API, frontend Angular, database **SQL Server only** for now (no Redis, no broker, no time-series database). | Owner |
| D2 | Modular monolith, Clean Architecture, DDD, CQRS through MediatR. | Owner |
| D3 | MediatR is pinned to **12.5.0**, the last release under Apache-2.0. Versions 13+ need a commercial or Community licence key in production; moving to them is an owner decision. | Plan |
| D4 | JWT authentication for users; a separate JWT type for devices. | Owner |
| D5 | Multi-tenant by `TenantId` on every tenant-owned row, with EF Core global query filters and write guards (same approach as the Licensing Platform). | Owner + Plan |
| D6 | Layout mirrors the Licensing Platform: layer projects (`SharedKernel`, `Domain`, `Application`, `Infrastructure`, `Api`) with one folder and one database schema per module; module boundaries are enforced by architecture tests. | Plan |
| D7 | In Monitor Cloud a **Tenant is a customer organisation** (e.g. "Acme Corporation"). It maps 1:1 to a **Customer** in the Licensing Platform. The Licensing "Tenant" is the vendor. | Plan |
| D8 | The word in code and UI is **Location** (as in the designs), not Site. Device Groups are out of scope until a later phase. | Plan |
| D9 | Customer roles are the four in the designs: Administrator, IT Manager, Technician, Report Viewer. Platform roles: Platform Admin, Platform Support. | Plan |
| D10 | **Enrollment = activation.** The agent sends the customer's product key to Monitor Cloud; Monitor Cloud activates the seat in the Licensing Platform with its own API client and derives the tenant from the licence. The agent holds no Licensing credentials. | Plan |
| D11 | Device identity is a 256-bit secret issued at enrollment, stored hashed, exchanged for a 60-minute device JWT. Key-pair credentials are a later hardening step. | Plan |
| D12 | Thresholds and monitor points are evaluated **on the agent**; the cloud evaluates only what the agent cannot see (device offline, licence state). | Plan |
| D13 | The first release has **alerts**, not the full incident workflow. The chart titled "Incident Trend" in the designs shows alerts opened per day. | Plan |
| D14 | Health score = healthy devices / total devices, as a percentage. An offline device has health `Unknown` and counts as not healthy. | Plan |
| D15 | Telemetry history is stored as one wide row per device per minute in SQL Server, written with `SqlBulkCopy`; live values are kept in memory and pushed over SignalR. Interfaces allow Redis/TimescaleDB later. | Plan |
| D16 | Platform staff enter a customer's workspace with the `X-Tenant-Id` header (allowed for platform roles only). Opening a workspace is audited with a reason. | Plan |
| D17 | Seed data uses invented company names with the same shape as the designs (see 08). | Plan |
| D18 | UI languages: English (default) and Arabic with RTL. All CSS uses logical properties from the first commit. | Plan |
| D19 | An **unlicensed** device keeps reporting status, basic metrics and alerts (as the designs show) for a grace period of 14 days. During that time its history is limited to 24 hours and monitor-point editing, configuration, reports and remote actions are locked. After the grace period its telemetry is dropped and its health becomes `Unknown`. | Plan |

## 5. Glossary

| Term | Meaning in Monitor Cloud |
|---|---|
| Tenant / Customer | A customer organisation. `Tenant` in code, "Customer" in the UI. |
| Location | A physical place of a customer (Cairo HQ). Owns devices. |
| Device | One machine running the agent. |
| Monitor Point | Something the agent checks from a device: a website, database, service, application, disk or network target. |
| Alert | A detected condition on a device, identified by `(DeviceId, IssueKey)`. Open until cleared or resolved. |
| Notification | A message shown or sent to a person about an alert or platform event. |
| Entitlement | What the customer's subscription allows: plan, features, device limit, dates. Owned by the Licensing Platform. |
| Seat | One device activation counted against the licence limit. |
| Connection state | `Online` or `Offline`. |
| Health | `Healthy`, `Warning`, `Critical` or `Unknown` - the highest severity among a device's open alerts. |
| Licence state | `Licensed` or `Unlicensed` (seat released, licence expired, suspended or revoked). |
| Workspace | A customer's area as seen by platform staff ("Open Workspace"). |

## 6. Out of scope for this plan

Incident lifecycle and Support Center, ticketing and chat, policy hierarchy and device groups, patch
management, remote terminal, SMS/WhatsApp delivery (the settings screen stores the switches; only e-mail
and in-app are delivered), billing, public API. They stay in the product roadmap and must not be
half-implemented here.

## 7. Facts verified in the existing code

These were read from the repositories on 6 October 2026 and the plan depends on them. Re-check them if
either repository has moved on.

**Licensing Platform** (`.NET 10`, EF Core 10, SQL Server, Angular 21):

- Device endpoints `POST /api/v1/licensing/{activate|validate|heartbeat|deactivate}` require a client token
  (`POST /api/v1/auth/client-token` with `clientId` / `clientSecret`) with scope `licenses.activate` or
  `licenses.validate`. The tenant is taken from the token.
- `activate` takes `productKey, deviceId, deviceName, productCode, appVersion, os` and an optional
  `Idempotency-Key` header. `deviceId` must be 8-128 characters of letters, digits, `- _ : .`.
- The response is `status, licenseNumber, productCode, planCode, features[], expiresAt, maxActivations,
  activeActivations, checkAfter, offlineValidUntil, token, kid, alreadyActivated`.
- The signed token is ES256. Claims: `sub` = device id, `lic`, `lid`, `tid` (the vendor tenant), `product`,
  `plan`, `features`, `check_after`, `lic_exp`; `aud` = product code; `exp` = offline validity.
  Public keys: `GET /api/v1/signing-keys` (anonymous).
- Errors are RFC 7807 with `code`: `LIC_INVALID_LICENSE`, `LIC_SUSPENDED`, `LIC_REVOKED`, `LIC_EXPIRED`,
  `LIC_SUBSCRIPTION_EXPIRED`, `LIC_SUBSCRIPTION_INACTIVE`, `LIC_ACTIVATION_LIMIT_REACHED`,
  `LIC_DEVICE_NOT_ACTIVATED`, `LIC_INVALID_DEVICE`, `RATE_LIMITED`.
- Customers, subscriptions, plans and licences can be read **only with a portal-user token** today. The
  scopes `licenses.read` and `licenses.issue` exist but no endpoint accepts them.
- Webhooks can be registered but are **not delivered** yet. Domain events are written to an outbox table.
- Rate limit on licensing endpoints: 300 requests per minute per API client.

**Agent** (`.NET 8`, Worker service + local HTTP API on 127.0.0.1:5050 + Avalonia desktop):

- Collects CPU, RAM, disks, network, sensors, processes, programs, users, services, monitor points and
  issues; stores one sample per minute in SQLite; evaluates thresholds locally.
- Has no cloud connection apart from an unauthenticated configuration pull that has no effect.
- Talks to the Licensing Platform directly with an API client id and secret stored in `appsettings.json`.
- Its licence-token parser does not read `sub` as the device id nor `lic_exp` as the licence expiry.
