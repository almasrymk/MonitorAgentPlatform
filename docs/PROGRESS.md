# Progress

Updated at the end of every milestone (CLAUDE.md, "When you finish a milestone").

## Working agreement for this run

- The product owner asked for the milestones to run back to back without stopping for review between them.
  Each milestone still ends with this file updated, a commit and a push to `main`.
- Development machine: Windows 11, .NET SDK 10.0.202, Node 22.19, local SQL Server 2022 (default instance).
  Docker is **not** installed, so tests use the local SQL Server through `MONITOR_TEST_SQLSERVER`
  (allowed by 09 section 1). CI keeps the SQL Server service container.

## M0 - Repository bootstrap (done 2026-10-06)

### Tasks

| Id | Status | Notes |
|---|---|---|
| MC-001 | Done | `MonitorCloud.slnx` with the projects of 01 section 1 (plus `tests/Shared` for fixtures and builders), `Directory.Build.props` (net10.0, nullable, warnings as errors, analyzers), `Directory.Packages.props` with exact versions (MediatR 12.5.0, EF Core 10.0.12), `.editorconfig`, `.gitignore`, `.gitattributes`, `.gitleaks.toml`, local tool manifest (`dotnet-ef`, `reportgenerator`) |
| MC-002 | Done | `Entity`/`AggregateRoot` (Guid v7), `ValueObject`, `Result`/`Result<T>`, `Error` + `ErrorKind`, `DomainException`, `Guard`, `IDomainEvent`/`DomainEvent`, `ITenantOwned`, `ILocationScoped` |
| MC-003 | Done | Serilog, `X-Correlation-Id` (validated, echoed, in every log event), RFC 7807 problem details with `code` and `traceId` on every error (exception handler, status-code pages, model binding), `/health/live`, `/health/ready` (database + outbox lag), `/openapi/v1.json`, CORS from configuration, security headers (CSP, nosniff, frame, referrer, HSTS outside Development), `Jwt` options validated on start (keys >= 32 bytes, distinct). Launch profile `http` on port 5300 |
| MC-004 | Done | `AppDbContext` (write side, read side, unit of work), one schema per module, tenant/location query filters built by reflection, cross-tenant write guard, domain events to `messaging.OutboxMessages` in the same save, `messaging.InboxMessages`, `audit.AuditRecords`, migration `InitialCreate` (history table in `messaging`), `OutboxDispatcher` (2 s poll, exponential back-off, dead-letter after 8 attempts, inbox per handler), outbox-lag health check |
| MC-005 | Done | MediatR 12.5.0; `ICommand`/`ICommand<T>`/`IQuery<T>` returning `Result`; behaviours in order: Logging, Authorization, Validation, Entitlement, UnitOfWork, QueryCaching |
| MC-006 | Done | `SqlServerFixture` (Testcontainers or `MONITOR_TEST_SQLSERVER`, databases `MonitorCloud_Test_{guid}` dropped at the end), `TestApp` (WebApplicationFactory, own database per test class, migrations at start), `TestClock`, `TestDatabase.ResetAsync` (DELETE script from the EF model), builders (`TestCurrentUser`, `TestTenantContext`), generic endpoint suites driven by `EndpointDataSource`, the permission matrix of 03 as test data |
| MC-007 | Done | Architecture tests 1-10 of 09 section 3 |
| MC-008 | Done | Angular 21.2 workspace in `portal/` (zoneless, standalone, OnPush, strict), port 4300 with proxy for `/api`, `/hubs`, `/openapi`, tokens copied to `src/styles/_tokens.scss`, self-hosted Inter / IBM Plex Sans Arabic / JetBrains Mono (`@fontsource`), `I18nService` (en bundled, ar lazy, `dir`/`lang` on `<html>`), `ThemeStore` (dark default), empty `AppShell` with build version in the sidebar footer, ESLint (angular-eslint, no `any`, OnPush), logical-CSS check, gzip bundle-budget check, Playwright |
| MC-009 | Done | `.github/workflows/ci.yml`: `backend` (SQL Server service container, vulnerable-package gate, build with warnings as errors, unit + architecture, integration + gateway + contract, coverage gates, pending-migration check), `portal` (lint, unit tests with coverage gate, production build, gzip budgets), `e2e` (Playwright), `secrets-scan` (gitleaks on full history) |
| MC-010 | Done | `docs/adr/0001-record-decisions.md` (D1-D19), `docs/operations.md` skeleton, `README.md` |

### Test results (local, 2026-10-06)

| Suite | Count | Result |
|---|---|---|
| Backend unit (`MonitorCloud.UnitTests`) | 135 | passed |
| Architecture (`MonitorCloud.ArchitectureTests`, includes 220 module-pair cases) | 245 | passed |
| Integration (`MonitorCloud.IntegrationTests`, local SQL Server) | 28 | passed |
| Gateway (`MonitorCloud.GatewayTests`, protocol contract only until M4) | 3 | passed |
| Contract (`MonitorCloud.ContractTests`) | 0 | no tests until M2 |
| Portal unit (Vitest) | 13 | passed |
| Portal e2e (Playwright, Chromium) | 3 | passed |

Coverage (merged Cobertura): Domain 100% line; Application 88.9% line / 91.1% branch; backend 87.1% line.
Portal: 86.5% statements. All gates of 09 met.

Acceptance:

- `dotnet build MonitorCloud.slnx -c Release -warnaserror`: 0 warnings, 0 errors.
- `/health/ready` returns `Healthy` with the local SQL Server (database and outbox checks).
- `npm start` shows the empty shell with the dark theme (verified by e2e test 1).
- CI on a pull request: the workflow is in place. A local run of every CI step passed (build, tests, coverage
  gates, vulnerable packages, pending-migration check, lint, unit, production build, gzip budgets, e2e), apart
  from gitleaks, which is not installed locally. The first GitHub run happens on this push (see "Open questions").

### Stubbed or deferred, with reason

- `IEntitlementReader` is `NoEntitlementReader` (no tenant has any feature) until the Licensing module (M2, MC-203).
- `AnonymousAccessTests`, `ProblemDetailsTests` and the authorization consistency checks run over every endpoint
  now. The per-role, cross-tenant, location-scope, platform-scope, device-token and paging HTTP checks need
  sign-in. They are added with the first business endpoints in M1, as 09 section 4 plans.
- `api:types` (`openapi-typescript`) is wired as an npm script. Its CI freshness check starts in M1, when the
  API has business endpoints and `schema.d.ts` exists.
- The `gateway` readiness check and the AgentGateway service start in M4. `MonitorCloud.AgentProtocol` already
  generates the `monitor.agent.v1` types from `proto/` (the 3 gateway tests guard the contract).
- `tools/MonitorCloud.DeviceSimulator` (M4) and `tools/MonitorCloud.LicensingSeeder` (M2) are placeholders
  that exit with code 1.
- **Design PNGs are missing** from `docs/design/` (only `tokens.scss` is in the repository). M0 needs only the
  tokens. Screenshot baselines (M3, M5, M6, M10) and the side-by-side checks need the images.

### Deviations and decisions

- ADR 0002 (proposed): three more authorization attributes (`AllowAnonymousRequest`, `AllowAuthenticatedUser`,
  `SystemOnly`), and `ENTITLEMENT_EXPIRED` applies only to feature-gated commands. Needs product-owner review.
- Angular budgets in `angular.json` measure raw size. The gzip limits of 07 section 2 are enforced by
  `npm run build:check` (`scripts/check-bundle-size.mjs`).
- npm 10.9.3 on the development machine fails with an internal arborist error (`edgesOut`). The lock file was
  produced with npm 11 (`npx npm@11 install`). It is lockfile v3 and works with `npm ci` on Node 22 in CI.

### Open questions

- ~~CI status on GitHub~~: the first run (commit 4079f02) finished with **success** on all jobs.
- Design images `docs/design/00..07-*.png` are needed before M3's screenshot baselines.

## M1 - Identity, tenancy and the two portals' shell (done 2026-10-06)

### Tasks

| Id | Status | Notes |
|---|---|---|
| MC-101 | Done | `User` (platform/tenant, invitations, lock-out 5 failures / 15 min, last-administrator rule, location scope), `RefreshToken` (HMAC-SHA256, 14 days, rotation, family revocation on re-use, audited), roles and permissions as code (03 section 2), PBKDF2 hashing, HS256 access tokens with the claims of 03, login / refresh / logout / me / password / language / invitation accept, development e-mail sender (log + `App_Data/mail`) |
| MC-102 | Done | `HttpCurrentUser`, `TenantContextMiddleware` (tenant users, platform workspace via `X-Tenant-Id`, devices, closed anonymous scope; foreign `X-Tenant-Id` rejected and audited), query filters for tenant-owned, location-scoped, location and optionally-tenant-owned rows, write guard for all of them; two JWT schemes (`Bearer`, `Device`); fallback policy requires a user token |
| MC-103 | Done | `Tenant` (create / update / suspend / resume / archive, events), `Location` (default "Unassigned" created by the `TenantCreatedV1` handler, delete rules, unique code per tenant), platform tenant endpoints, workspace sessions (reason >= 10, audited open/close); suspended or archived customers lose their refresh tokens (event handler) |
| MC-104 | Done | Audit list endpoints `/platform/audit` and `/audit` (filters, paging) |
| MC-105 | Done | `BootstrapSeeder`; `DemoSeeder` part 1: 2 platform users, the 8 detailed and 40 generated customers (2 suspended), their locations and users (Acme has 8); `dotnet run --project src/MonitorCloud.Api -- seed --reset` |
| MC-106 | Done | Tenant users (list with role/status/search, invite, update, activate, deactivate, resend invitation), `/roles`, platform users (list, create, update, activate, deactivate, reset password) |
| MC-107 | Done | Login (language switch, translated errors incl. minutes of lock-out), accept-invitation, `AuthService` (access token in memory, refresh token in `sessionStorage`, silent restore, single shared refresh), guards (auth, area, permission, workspace), interceptors (bearer, `X-Tenant-Id` only inside a workspace and never on platform/auth calls, refresh once on 401 with queued retries, correlation id, problem details -> translated toast), `ScopeStore`, contextual sidebar, top bar (search placeholder, date range, bell placeholder, user menu with language and sign-out), breadcrumb, workspace banner, `/dev/components` |
| MC-108 | Done | `Card`, `KpiTile` (`goodWhen`), `StatusPill`, `Button`, `SearchInput`, `Select`, `Tabs`, `DataTable` (sortable headers, sticky header, loading/empty), `Pagination`, `Dialog`, `ConfirmWithReasonDialog`, `Drawer`, `Toast`, `Skeleton`, `EmptyState`, `ErrorState`, `Avatar`, `EntityHeader` (+ `PageHeader`, `Icon`) |
| MC-109 | Done | Users & Permissions (tabs with counts, invite/edit drawer, row actions), Users & Roles (platform), Locations (cards, add/edit drawer), Customers (tiles, toolbar, cards and list view, Open Workspace / Suspend / Archive with reason, Reactivate); device, health, licence and renewal fields show skeletons until M2-M5 |

### Test results (local, 2026-10-06)

| Suite | Count | Result |
|---|---|---|
| Backend unit | 203 | passed |
| Architecture (module isolation now scans IL with Mono.Cecil; error codes translated in both dictionaries) | 253 | passed |
| Integration (generic suites: anonymous access, permission matrix for 6 roles x every endpoint, cross-tenant, location scope, platform scope, device tokens, problem details, paging/sorting; features: auth, users, tenants, locations, data isolation, seed) | 110 | passed |
| Gateway | 3 | passed |
| Portal unit (Vitest) | 82 | passed |
| Portal e2e (Playwright against the API with the demo seed: flows 5 and 6, workspace with reason and banner, `/admin` blocked for a tenant user, reload/sign-out, RTL) | 10 | passed |

Coverage: Domain 99.1% line / 92.9% branch, Application 97.4% line / 89.7% branch, backend 97.3% line; portal 85.7% statements.

Accept criteria: `admin@monitor.local`, `admin@acme.test` and `viewer@acme.test` sign in; `admin@oasis.test` is rejected
(`AUTH_TENANT_SUSPENDED`); the platform admin opens the Acme workspace with a reason and sees the banner; a tenant
user cannot reach `/admin`; the five generic suites and the refresh-token re-use test pass; e2e flows 5 and 6 pass.

### Stubbed or deferred, with reason

- `ILocationDeviceCounter` returns 0 (`NoDevicesCounter`) until the Devices module (M3): `LOCATION_NOT_EMPTY`
  is therefore never returned yet. Location cards show 0 devices.
- Customer cards: plan, devices, health, licence usage and next renewal are `null` (skeletons) until M2/M3;
  "Expiring Soon" is 0 until M2.
- Top-bar search box and bell are placeholders (search in M9, notifications in M6). Dashboards, devices,
  subscription, reports, archive and settings menu items open a "delivered in milestone Mx" page.
- `DataTable` virtual scrolling (above 100 rows) arrives with the device lists in M3.
- Gateway half of `DeviceTokenTests` (user token rejected by the gateway) arrives with the gateway in M4.
- Invitation e-mails use the development sender; real delivery with retry is MC-603 (M6).

### Deviations and decisions

- Handlers that return a versioned DTO save before mapping so the returned `version`/`ETag` is the new row
  version (the unit-of-work behaviour then has nothing left to save).
- Sign-in, refresh, logout and invitation acceptance run under the system scope inside their handlers (allowed
  `IgnoreQueryFilters` call sites in `Application/Identity/`); a failed sign-in saves the failure counter and the
  audit record explicitly because the command result is a failure.
- `TENANT_SCOPE_REQUIRED` is returned with HTTP 400 as listed in 06 section 6.
- Unknown routes without a token answer 401 (fallback authorization policy), so routes do not leak.
- Requests of one feature are grouped in one file per area (command, validator and handler side by side,
  e.g. `Application/Tenancy/LocationRequests.cs`) instead of one folder per request. ADR 0003 (proposed).
- `api:types` freshness is checked in the CI `e2e` job, which runs the API with the demo seed.

### Open questions

- ADR 0002 and ADR 0003 are proposed and await product-owner review.

## M2 - Licensing integration (done 2026-10-07)

### Tasks

| Id | Status | Notes |
|---|---|---|
| LP-1..LP-5 | Done | In `almasrymk/LicensingPlatform`, branch `feature/monitor-cloud-integration` (commit d41f3b3, pushed; `main` untouched): scopes `customers.read`, `subscriptions.read`, `catalog.read`; `licenseId`/`customerId`/`subscriptionId` on activation/check responses and the `cid` token claim; `api/v1/integration` (customers, entitlements, plans, activations, heartbeat and release by licence id, change feed over the outbox with a new `CustomerId` column and migration `IntegrationChangeFeed`); ADR-012. 11 new tests; all 145 Licensing tests pass on SQLite, integration and licensing tests also on SQL Server |
| MC-201 | Done | `ILicensingGateway` + DTOs (04 section 2), error codes passed through unchanged; `FakeLicensingGateway` over `FakeLicensingStore` (limits, statuses, idempotency keys, ES256 tokens, JWKS signing keys, change feed, test hooks for plan/status changes, outage and rate limiting); `seed/licensing-fake.json` and `seed/README.md` (demo product keys) generated by `DemoSeeder` |
| MC-202 | Done | `HttpLicensingGateway`: client-credentials token cached until one minute before expiry and renewed once on 401; resilience handler (10 s timeout, 3 retries with jitter on 5xx/timeouts only, circuit breaker); transport errors -> `LICENSING_UNAVAILABLE`, 429 -> `RATE_LIMITED` |
| MC-203 | Done | `TenantEntitlement` (derivation of 04 section 4.3, `EntitlementChangedV1` only on real changes), `DeviceLicense` (used from M3), `LicensingSyncState`; `LicensingSyncService` (change feed with cursor saved with the changes, full reconcile every `FullReconcileHours`, failure counting) and `LicensingSyncJob`; `EntitlementReader` behind the `EntitlementBehavior` (fail open on outages, cache invalidated by the event); `licensing` readiness check degraded after 5 failures |
| MC-204 | Done | `GET /subscription`, `GET /platform/plans` (with customer counts), `GET /platform/licensing/status`, `POST /platform/licensing/sync` |
| MC-205 | Done | `ITenantProvisioning` (tenant for a new Licensing customer, unique code, default location and entitlement through the outbox); creating a tenant with `licensingCustomerId` loads its entitlement (`TenantCreatedV1` handler) |
| MC-206 | Done | `MonitorCloud.ContractTests`: one 16-test suite run against the fake (always) and the live platform when `LICENSING_CONTRACT_*` is set; nightly/manual CI job `licensing-contract`; `tools/MonitorCloud.LicensingSeeder` (product, plans, the detailed customers, contract scenarios, API client, writes `seed/licensing-live.local.json`) |
| MC-207 | Done | Portal: Subscription & Licenses (overview: current plan with Expiring pill and seat bar, licensed/unlicensed donut, usage by OS, upcoming renewal, "Contact your account manager" dialog, stale warning; device tabs show counts and an empty state until M3), Plans (read-only, licensing status, link to the Licensing Platform, Sync now), customer cards with plan, Expiring/Suspended pill, licence usage bar and next renewal, plan and subscription-status filters ("Expiring" = renewal within 30 days) |

### Test results (2026-10-07)

| Suite | Count | Result |
|---|---|---|
| Backend unit | 245 | passed |
| Architecture | 253 | passed |
| Integration (local SQL Server; 13 new licensing tests) | 123 | passed |
| Gateway | 3 | passed |
| Contract, fake | 16 | passed |
| Contract, live: local Licensing Platform from the LP branch (temporary worktree and database, seeded with `LicensingSeeder`) | 14 + 2 not applicable | passed (expired-licence and rate-limit scenarios are not set up live) |
| Portal unit | 88 | passed |
| Portal e2e (adds subscription and plans/customer-card flows) | 12 | passed |

Coverage: Domain 99.1% line, Application 97.6% line / 89.7% branch, backend 95.0% line; portal 86.2% statements.

Acceptance: contract suite green against the fake and against a local Licensing Platform with LP-1..5; a plan change
and a subscription suspension in the fake are visible in `/subscription` after one sync with exactly one
`EntitlementChangedV1` (test); a `[RequiresFeature("monitorpoints")]` command returns `FEATURE_NOT_ENTITLED` for the
Starter tenant (test command); during a Licensing outage cached entitlements stay, failures are counted and
`/health/ready` reports `licensing` degraded after 5 failures (test).

The contract run found one real difference: the live `GET /signing-keys` answers `{ keys: [JWK], pem: [...] }`; the
fake answered a plain list. The fake now matches the live shape.

### Stubbed or deferred, with reason

- Platform notifications (5 sync failures, fail-open after `FailOpenHours`, "New customer provisioned") need the
  Notifications module (M6); today these situations are audited and logged.
- "Suspended / Cancelled subscription: tenant users see the subscription screen only" (04 section 4.5) is not
  enforced yet; feature-gated commands are refused when expired. Planned with the remaining entitlement rules in M3
  (unlicensed grace, MC-304).
- Device counts in Subscription & Licenses (licensed/unlicensed, by OS) come from the Devices module (M3);
  `IDeviceLicenseStats` returns zeros until then. Seat counts come from Licensing and are correct now.
- Licensing `updatedSince` on customers uses the creation time (the Licensing customer has no modification time).

### Notes for the product owner

- The LP changes are on a branch, not merged: review and merge `feature/monitor-cloud-integration` in the Licensing
  Platform when ready. Monitor Cloud runs in `Fake` mode until then.

## M3 - Devices and enrollment (done 2026-10-07)

### Tasks

| Id | Status | Notes |
|---|---|---|
| MC-301 | Done | `Device`, `DeviceCredential` (HMAC hash, 5 failures in 10 min block the id for 10 min), `DeviceState` (read model with `UnlicensedSince`, ADR 0004), `InventoryDocument` (Brotli JSON + SHA-256); `GET /devices` (filters location, search, os, status, license; sorts severity, name, lastSeen, cpu; paging), `/devices/summary`, `/devices/{id}` with ETag, `PUT` rename/move with `If-Match`, `retire`, `unlicense` |
| MC-302 | Done | `LocationEnrollmentCode` (`LOC-XXXXXX-XXXXXX`, 60 bits, only hash and prefix stored, shown once); create/list/revoke under `/locations/{id}/enrollment-codes` with install commands for Windows, Linux and macOS |
| MC-303 | Done | `POST /api/agent/v1/enroll` (enrollment = activation, tenant provisioned for a new Licensing customer, location code, re-enrollment issues a new secret, retired device reactivated in its old location, the seat is released again when enrollment fails afterwards, `LIC_*` codes passed through), `/token` (device JWT, 60 min, own key and audience), `/credential/rotate` (device token only); limits: 10/min per IP, 5/h per fingerprint; `EnrollmentAttempt` log (key prefix only, 90 days) |
| MC-304 | Done | `DeviceMaintenanceJob` every 5 minutes: refreshes due licences (renew, `Unlicensed` on rejection, retry in an hour when Licensing is unavailable), applies the D19 grace rule (health `Unknown` after 14 days), removes old attempts; seat released through the outbox on retire and unlicense |
| MC-305 | Done | `DemoSeeder` part 2: 2,373 devices over the 48 customers with state, metrics, licence rows (fake ES256 tokens) and the inventory of the 8 fixed Cairo HQ devices. Acme matches 08 exactly (per-location online, health, offline, unlicensed and Cairo HQ OS counts); licensed fingerprints equal the seats of the fake licences |
| MC-306 | Done | `/dashboard`, `/locations/{id}/dashboard`, `/platform/dashboard`, device counts on `/locations` and `/platform/tenants` (plus the health filter); incident and alert blocks return empty series until M6 |
| MC-307 | Done | `DeviceCard`, `CustomerCard`, `LocationCard`, `OsIcon`, `ProgressBar`, `HealthBar`, `RingGauge`, `DonutChart` (Apache ECharts 6.1.0, loaded on demand, legend and text summary), `HBar`, `ViewToggle`, `CountBadge`, `CopyButton`, `Menu` |
| MC-308 | Done | Customers (cards complete, health filter), Customer dashboard / workspace overview, Locations (cards), Location overview, Location devices with Add Device dialog and rename/move/unlicense/retire, Devices (all) with location filter, Subscription device tabs (all / licensed / unlicensed). The device screen itself is M5 (route shows "coming soon") |

### Test results (2026-10-07)

| Suite | Count | Result |
|---|---|---|
| Backend unit (62 new: device, credential, state, health truth table, enrollment code, attempt, inventory) | 307 | passed |
| Architecture | 253 | passed |
| Integration (47 new: enrollment, device credentials, devices, enrollment codes, refresh and grace jobs, seeded dashboards) | 170 | passed |
| Gateway | 3 | passed |
| Contract, fake (live: skipped without `LICENSING_CONTRACT_*`) | 16 | passed |
| Portal unit | 120 | passed |
| Portal e2e (6 new device and dashboard flows) | 18 | passed |

Coverage: Domain 99.3% line, Application 97.7% line / 89.4% branch, backend 96.2% line; portal 88.5% statements.

Acceptance: every tile and card number equals an independent SQL count over the seed (`SeedDashboardTests`);
the enrollment tests of 09 section 2 and the device-token tests pass; the device list p95 is below 150 ms over
100 requests on the seed (`Device_list_p95_is_below_150_ms_over_100_requests`).

### Stubbed or deferred, with reason

- **Screenshot baselines for Customers and Location devices are not created.** The design images in `docs/design/`
  are missing from the repository, so there is nothing to review a baseline against. Baselines would also differ
  between Windows and the Linux CI runner. The screens were checked against the plan's block lists (07 sections
  5.2-5.6) on the seed.
- Incident Trend, Incidents by Severity, Recent Alerts and alert-based issues show empty states until alerts exist
  (M6). The Platform Admin Dashboard screen stays "coming soon"; its API is ready.
- Device screen (`/devices/:id`): M5. Live updates of cards (`deviceStateChanged`): M4/M5.
- Seeded devices have no credentials (the device simulator of M4 enrolls its own devices).
- Default device configuration at enrollment (05 section 1.1) arrives with the Configuration module (M8).

### Notes for the product owner

- ADR 0004 records five details of the Devices module (state column, licence aggregate, enrollment-code location,
  default location at provisioning, one maintenance job).
- CI's e2e job raises the login rate limit (`RateLimiting__Auth__PermitLimit`), because every e2e test signs in.
- Device lists now ignore answers to superseded requests (a slow first page could overwrite a search result).

## M4 - Agent gateway and simulator (done 2026-10-07)

### Tasks

| Id | Status | Notes |
|---|---|---|
| MC-401 | Done | `MonitorCloud.AgentProtocol` generated from `agent.proto` (since M0, net8.0 + net10.0) |
| MC-402 | Done | `MonitorCloud.AgentGateway`: `IAgentTransport` + gRPC adapter, device token required (Device policy, tenant and device from the token only), `AgentSession`, in-memory `IAgentSessionRegistry`, `AgentMessageRouter` (handlers per message kind; guaranteed messages without a handler are not acknowledged until M5), Hello within 10 s (`HELLO_REQUIRED`), `PROTOCOL_UNSUPPORTED`, `DEVICE_RETIRED`, `CREDENTIAL_REVOKED`, `TENANT_ARCHIVED`, one session per device (`DUPLICATE_SESSION`, three replacements in 5 minutes are audited as a possible clone and raise `DeviceCloneSuspectedV1`), `SESSION_ROTATE` after 12 h, `SERVER_SHUTDOWN` with retry 5-30 s, retiring a device closes its stream |
| MC-403 | Done | `PresenceMonitor` every 10 s: no message for 3 heartbeats -> `HEARTBEAT_TIMEOUT` and Offline; a closed stream -> Offline after 15 s unless the device reconnects; Goodbye -> Offline at once with the reason; last contact written for connected devices. `DeviceCameOnlineV1` / `DeviceWentOfflineV1` |
| MC-404 | Done | SignalR hub `/hubs/live` (token in `access_token`), `SubscribePlatform/Tenant/Location/Device` checked against the caller's tenant and location scope; `deviceStateChanged` to tenant, location and device groups; `summaryChanged` coalesced to one per group every 2 s |
| MC-405 | Done | `tools/MonitorCloud.SimulatedAgent` (library: enroll, token, stream, Hello, heartbeats, Goodbye; used by `MonitorCloud.GatewayTests`) and `tools/MonitorCloud.DeviceSimulator` (`enroll --count`, `run --devices [--seconds]`; uses the demo product keys and creates the location enrollment code as the tenant administrator; device secrets in the git-ignored `seed/simulator.local.json`; reconnects with back-off and honours `retry_after_seconds`) |
| MC-406 | Done | Portal `LiveService` (SignalR client loaded on demand, reference-counted groups, re-joins after reconnect); device cards update in place, device tiles, the customer dashboard and the location pages refetch on `summaryChanged` |
| MC-407 | **Blocked** | Needs the intended production host to run `simulator run --devices 1` from outside its network (05 section 12). No host or credentials are available on this machine. **Owner action:** deploy the API to the target host and run the check; if HTTP/2 streams do not pass the reverse proxy, the WebSocket fallback behind `IAgentTransport` is the plan's answer (ADR needed then). Locally the gateway runs on its own HTTP/2 endpoint (`http://localhost:5301`, Development) next to REST/SignalR on 5300, because HTTP/2 without TLS needs a dedicated endpoint |

### Test results (2026-10-07)

| Suite | Count | Result |
|---|---|---|
| Backend unit | 307 | passed |
| Architecture | 253 | passed |
| Integration | 170 | passed |
| Gateway (20 new: gateway tests 1-3, 7 and 11 of 09 section 5, retire closes the stream, hub scope, shutdown) | 23 | passed |
| Contract, fake | 16 | passed |
| Portal unit (6 new: LiveService, live card updates) | 126 | passed |
| Portal e2e (new flow 2: simulated devices online, then offline without a reload) | 19 | passed |

Acceptance: gateway tests 1-3, 7 and 11 pass; `simulator run --devices 20` showed the 20 devices online in Cairo HQ,
and the e2e flow sees simulated devices go Offline through the live hub without a reload; the hosting check is
blocked (see MC-407).

### Stubbed or deferred, with reason

- The `device-offline` alert 2 minutes after a device goes offline (and its resolution on reconnect, `Info` for
  `HOST_SHUTTING_DOWN`/`UPDATING`) needs the Monitoring module (alerts, M5/M6); `DeviceWentOfflineV1` carries the
  reason for it. The platform alert "possible cloned device" likewise; today it is audited.
- Telemetry, inventory, issues, snapshots and live mode messages are routed but not yet handled (M5): they are not
  acknowledged, so agents keep them.
- `simulator --scenario` and `simulator load` come with telemetry (M5) and the load tests (M10).
- The device screen header reacts to live events once the device screen exists (M5).

### Notes for the product owner

- Run the hosting check of MC-407 on the target host before relying on gRPC in production.
- A bug found by the e2e flow: the portal missed the Offline change after a Goodbye because the broadcast was
  cancelled together with the agent's call. Live events no longer use the caller's cancellation.

## M5 - Telemetry and the device screen (done 2026-10-07)

### Tasks

| Id | Status | Notes |
|---|---|---|
| MC-501 | Done | Schema `telemetry`: `MetricMinutes`, `MetricHours` (page compression), `DiskUsageHours`, `LiveSnapshots`. `TelemetryWriter`: bounded channel (wait when full = back-pressure); flush every 2 s or 1,000 rows; `SqlBulkCopy` into staging, idempotent insert, hourly disk upsert, one set-based `DeviceStates` update (CPU, RAM, disk, uptime, last telemetry, last sequence); acknowledgements only after commit; retry with back-off while the database is down. Duplicate sequences are acknowledged and ignored; minutes beyond retention are acknowledged and dropped; unlicensed devices after the grace period are acknowledged and dropped (rule 9) |
| MC-502 | Done | `Snapshot` -> `ILiveSnapshotStore` (memory, persisted at most once a minute) + `snapshotUpdated`; `InventoryUpdate` -> `UpsertInventoryCommand` (stored only when the hash changes), acknowledged after the save |
| MC-503 | Done | `POST /devices/{id}/live-sessions`; `LiveModeController` sends `SetTelemetryMode(LIVE, 2 s, 60 s)` on first interest and again every 30 s while renewed; `LiveSample` -> `liveSample` to `device:{id}` (throttled, never stored) |
| MC-504 | Partly | `MetricRollupJob` (every 5 min, weighted averages, idempotent `MERGE`) and `TelemetryRetention` (daily, batches of 50,000) done. `MonitorPointSampler` moves to M6 with the monitor points it samples (ADR 0005) |
| MC-505 | Done | `/devices/{id}/overview` (snapshot), `/metrics` (minutes up to 6 h, else hours; max 1,500 points), `/disks` (latest per partition, Warning 85%, Critical 92%), `/inventory/{kind}`; resource averages of the location dashboard from `DeviceStates` (M3) |
| MC-506 | Done | `DemoSeeder` part 3: 237,720 hourly rows (7 days, the eight detailed customers), 48,240 minutes (6 h, Cairo HQ online devices, the current values hold for the last 20 minutes so WEB-SRV-01 shows its 92% CPU plateau), disk usage for the fixed devices (C: of SQL-DB-01 filling), 134 snapshots, and the Cairo HQ enrollment code printed at seed time. Alerts, notifications, audit history, reports and archive of 08 section 4 come with M6 and M9 |
| MC-507 | Done | `mc-chart` (ECharts on demand), `TrendChart`, `Sparkline`, `SpeedGauge`, `Accordion`, `Timeline` |
| MC-508 | Done | Device details: header, live mode while visible and online, offline banner, Service Status, CPU / RAM / Disk / Network cards (gauge, facts, history for the top-bar range, top 5), Disk Status, Hardware & OS; Applications tab (Programs, Services, Users with search); About tab (masked fingerprint). Monitor Points, Reports and Settings tabs: M8/M9 |

### Test results (2026-10-07)

| Suite | Count | Result |
|---|---|---|
| Backend unit | 307 | passed |
| Architecture | 253 | passed |
| Integration | 170 | passed |
| Gateway (11 new: tests 4, 5, 8 and 12 of 09 section 5, rollup, retention, device screen queries) | 34 | passed |
| Contract, fake | 16 | passed |
| Portal unit (9 new) | 135 | passed |
| Portal e2e (new flow 3: live samples every 2 s while open, none after closing) | 20 | passed |

Coverage: Domain 98.6% line, Application 95.3% line / 84.3% branch, backend 95.9% line; portal 89.3% statements.

Load test (`docs/perf/2026-10-07.md`): 1,500 simulated devices for 10 minutes on a 4-core laptop. No data loss
(14,987 of 14,987 batches stored), acknowledgement p95 2.0 s, device list p95 37 ms and dashboard p95 61 ms during
the load, gateway memory 300-337 MB.

### Stubbed or deferred, with reason

- **The design image of the device screen is missing.** As in M3, there is no screenshot baseline; the screen
  follows the block list of 07 section 5.7.
- Monitor points carousel and Messages & Issues board need the Monitoring module (M6).
- `MonitorPointSampler`: M6 (see MC-504).
- The `clock-skew` alert (rule 8) needs alerts (M6).

### Notes for the product owner

- ADR 0005 records five telemetry details. Among them: a `DiskPercentMax` column, and a separate HTTP/2 port for
  the gateway in Development.
- The device screen needs no extra configuration. Live mode stops by itself 60 s after the screen closes.
## M6 - Alerts and notifications (done 2026-10-08)

### Tasks

| Id | Status | Notes |
|---|---|---|
| MC-601 | Done | Module `monitoring`: `Alert` (Raise / Touch / Acknowledge / Resolve; one open alert per device and issue key, filtered unique index), `AlertDailyStat` (tenant-local day), `MonitorPoint` + `MonitorPointState` imported from `MonitorPointReport` (a full report removes points the agent no longer has). `ApplyIssueEventCommand`: raised, severity changed, cleared, duplicates touch the open alert, backlog older than 15 minutes keeps its time and does not notify. The Devices module recomputes `DeviceStates` open counts and health from the alert events and pushes `deviceStateChanged`. `MonitorPointSampler` writes `telemetry.MonitorPointSamples` once a minute (from M5, ADR 0005) |
| MC-602 | Done | `device-offline`: a pending row when the device goes offline, raised by the monitoring job after the tenant's delay (default 2 min, severity from Settings > General). `HostShuttingDown` and `Updating` use Info. Coming back online cancels or resolves it. When every device of a location goes offline within 60 s, one location notification replaces the per-device notifications. `license` (follows the seat, without changing the health during the grace period), `clock-skew` (gateway, first message of a session and every change), `clone-suspected` (rule 3) with a platform notification. Retired devices resolve their alerts; moved devices take them along |
| MC-603 | Done | Module `notifications`: in-app feed (tenant, user, location scope), unread count, read state per user, `AlertChannelSettings` (e-mail and in-app on by default, SMS "Not available yet", webhook needs `notifications.webhook`), recipients (All / WarningsAndCritical / CriticalOnly, optional location), e-mail deliveries for plans with `notifications.email`, sent by the job with 5 attempts (1, 2, 4, 8 min) and a delivery log. Webhook sending is stored but not sent yet (ADR 0006, point 10) |
| MC-604 | Done | `/alerts` (filters, sort, paging), `/alerts/{id}`, acknowledge, resolve (`ALERT_SELF_RESOLVING` for `device-offline` and `license`), `/devices/{id}/alerts`, `/devices/{id}/monitor-points`, `/notifications`, unread-count, read; `/platform/alerts`, `/platform/notifications` (+ unread-count and read, ADR 0006); `/settings/general`, `/settings/alerts`, recipients CRUD. SignalR `alertRaised` / `alertUpdated` / `alertResolved` / `notificationCreated`. Gateway: `IssueEvent`, `MonitorPointReport`, clock skew, `LicenseUpdate` push after a licence change |
| MC-605 | Done | Incident trend (7 / 30 days, from `AlertDailyStats`), incidents by severity with the resolved count, recent alerts on the three dashboards. Top problematic devices and recent activity were done in M3 |
| MC-606 | Done | `DemoSeeder` part 4: open alerts equal to the seeded health counts (the fixed Cairo HQ devices get the alerts of 08 section 3), resolved alerts over 30 days peaking three days ago, `AlertDailyStats` from both, 25 notifications per detailed customer and for the platform with 3 unread, 60 platform audit records over 7 days, six monitor points on WEB-SRV-01 (one Critical), alert recipients |
| MC-607 | Done | Notifications screen (platform, customer, location tab): severity and period filters, mark one / all as read. Bell with unread count (live). Recent Alerts and Incident Trend on every dashboard. Device screen: Monitor Points carousel (read-only, detail on selection), Messages & Issues board. Settings: General and Alert Settings tabs (Monitoring, Locations, Integrations in M8 / M9) |
| MC-608 | Done | Platform Admin Dashboard (07 section 5.1) with all blocks; Customer workspace and Location overview now show their alert blocks |

### Test results (2026-10-08)

| Suite | Count | Result |
|---|---|---|
| Backend unit (24 new: alert, statistics, monitor points, settings, recipients, deliveries) | 331 | passed |
| Architecture | 253 | passed |
| Integration (13 new: alerts, notifications, settings, recipients, retries, seed part 4) | 183 | passed |
| Gateway (13 new: test 6, storm of 500 raises, severity change, backlog, reserved keys, isolation, test 7 offline alerts, delay setting, location outage, clock skew, monitor points and sampler, test 10) | 47 | passed |
| Contract, fake | 16 | passed |
| Portal unit (8 new) | 143 | passed |
| Portal e2e (4 new: simulator issue -> alert, bell, feed, resolve; mark all read; Platform Admin Dashboard; recipients) | 24 | passed |

Coverage: Domain 98.7% line, Application 93.6% line / 80.5% branch, backend 96.0% line; portal 87.6% statements.

Storm test: 500 repeated raises of one issue give one alert with `Occurrences = 500` and one notification. The
e2e flow sees the simulator's issue on the device screen, the health turn Critical, the bell go up by one, and the
alert resolve when the simulator clears it. The development sender wrote one e-mail per matching recipient.

### Stubbed or deferred, with reason

- **The design images are still missing** (`docs/design/01`, `03`, `04`). The three dashboards follow the block
  lists of 07 sections 5.1, 5.3 and 5.5; there is no screenshot comparison.
- Webhook delivery: the switch and URL are saved; sending arrives with the integrations of M9.
- Monitor points are read-only (add / edit by type is M8, as planned).

### Notes for the product owner

- ADR 0006 lists eleven details for review. The visible ones: the `license` alert does not change the device
  health, the platform feed holds platform notifications only, and a possible clone is a Warning alert on the
  device plus a platform notification.
- The simulator can raise an issue: `simulator run --devices 1 --issue cpu --issue-after 5 --clear-after 40`.

## M7 - Real agent end to end (in progress 2026-10-08)

Agent work happens in `almasrymk/UBGMonitor` on the branch `cloud/m7-connector`. The branch is **local only**
until the product owner allows the push (see Open questions). It never commits to `master`. That repository has
its own security plan (`docs/security/SECURITY_PLAN.md`) run by another agent. Its `AGENTS.md` says to stop and
report when work overlaps. The AG items that touch its runs are therefore deferred until the owner decides (ADR 0007).

### Tasks

| Id | Status | Notes |
|---|---|---|
| AG-0 | Done | All projects on `net10.0`; Microsoft packages 10.0.12; Serilog.Sinks.File 7.0.0; CI SDK 10.0.x; 0 warnings |
| AG-1 | Done before M7 | The licensing credentials were already removed from `appsettings.json` by the agent's security cleanup. Rotating the exposed secret stays an owner step (agent PROGRESS, F-01) |
| AG-2 | Done | `fingerprint` = the existing `DeviceFingerprint` (machine id hash, same as the Licensing device id) |
| AG-3 | Done | New project `MonitorAgent.Cloud`. It contains `CloudOptions`, the enrollment and token client (`CloudHttpClient`), `DeviceTokenProvider` (renews 5 min early), `GatewaySession` (gRPC), `CloudAgentService` (the message pump), `Outbox`, `MinuteAggregator`, `IssueChangeTracker`, `InventoryPublisher`, live mode and `CloudStatus`. `ConfigUpdate` is acknowledged; it is applied in M8. The service feeds it from its existing collectors (`ServiceCloudSource`) |
| AG-4 | Done | `proto/` copy, `proto/VERSION` (sha256 of the LF form), `scripts/sync-proto.ps1`, and a hash test |
| AG-5 | Done | Outbox table in `cloud.db` (ADR 0007): the row number is the protocol sequence. Size cap 200 MB; the oldest metric rows are dropped first, never issues, config or commands |
| AG-6 | Done | Samples every 5 s, then per minute: avg, max, P95 (nearest rank), averaged rates, highest disk use and temperature. The local reports are unchanged |
| AG-7 | Deferred | Agent security Run 5 (Licensing), which waits for owner decisions D1-D3. Cloud licence tokens arrive through `ICloudLicenseSink`, so `LicenseService` can take them over in that run |
| AG-8 | Deferred to M8 | One threshold evaluator fed by the cloud document needs the central configuration (M8). Seen in the CPU test: without hysteresis an alert flaps once around the threshold |
| AG-9 | Deferred | Agent security Run 2 (Local API) |
| AG-10 | Done | Back-off 1 s -> 5 min with +/-20 % jitter, `retry_after_seconds` honoured, system proxy used |
| AG-11 | Partly | `MONITORAGENT_CLOUDURL` / `PRODUCTKEY` / `LOCATION` are read and enroll on first start. MSI / deb / pkg properties wait for agent security Run 3 (Installation) |
| AG-12 | Done | 14 connector tests plus an end-to-end test against a running Monitor Cloud (`MONITORCLOUD_E2E_*`). Agent suite: 101 passed, 2 skipped (Unix-only, and e2e without its variables) |
| MC-701 | Done (waits for the push) | The cloud CI `e2e` job checks out the agent (`vars.AGENT_REF`, default `master`) and runs its end-to-end test against the CI cloud. It skips while that ref has no connector |
| MC-702 | Done | `docs/agent-integration.md`: configuration, enrollment, stored files, offline behaviour, every error code, logs |

### Acceptance (2026-10-08, Windows 11, this development machine)

| Check | Result |
|---|---|
| A real agent enrolls with a demo product key and a location code | Passed: `Enrolled as device ... (Acme Corporation / Cairo HQ)` |
| Appears in the right location; shows live data on its device screen | Passed: Cairo HQ, Online, Licensed. A snapshot every minute with the top-5 lists. Minutes and inventory (hardware, OS, network, programs, services) arrive |
| Raises and clears a CPU alert | Passed: CPU burned for 50 s, so `cpu` Critical opened, then resolved. Real alerts arrived as well (`disk-C` Critical at 91.9 %) |
| Survives a 10-minute network cut with no gaps in the minute history | Passed: cloud stopped 18:05-18:15; 18 consecutive minutes 18:03-18:20, 0 gaps; Online again |
| Keeps working locally while offline | Passed: the local checks and reports went on during the cut |
| The same on Linux | **Not verified**: there is no Linux machine here. The agent's CI builds and tests on ubuntu and macOS once the branch is pushed |

### Open questions for the product owner

1. May AG-1/7/9/11 stay with the agent's security runs (proposed), or should they be done in this branch?
2. May `cloud/m7-connector` be pushed to `almasrymk/UBGMonitor`? MC-701 and the Linux run need it.

## M8 - Central configuration (done 2026-10-08)

### Tasks

| Id | Status | Notes |
|---|---|---|
| MC-801 | Done | Module `config`: `DeviceConfiguration` (version and JSON document), `DeviceConfigurationAck` (last applied version, last rejected version and its reason) and `TenantConfigurationDefaults`. Schema validation: levels 1-100, critical above warning, clear level below warning, durations 0-3600 s, sample 1-30 s. Every change raises `DeviceConfigurationChangedV1`. The gateway pushes `ConfigUpdate` at once to a connected device, and after `Welcome` when the agent's applied version differs. `ConfigApplied` is recorded (also for restricted devices, rule 9) and shown live (`configApplied`). Enrollment creates the configuration from the tenant defaults. Agent-reported points are imported until the first edit in the portal; from then on the cloud owns them (ADR 0008) |
| MC-802 | Done | `GET/PUT /devices/{id}/configuration` (`ETag` / `If-Match` = version, 409 `CONCURRENCY_CONFLICT`). `POST/PUT/DELETE /devices/{id}/monitor-points` need `monitorpoints.manage` and the feature `monitorpoints` (403 `FEATURE_NOT_ENTITLED` for Starter). They validate per type (website URL, ping host, disk drive) and bump the version. `GET/PUT /settings/monitoring` holds the tenant default thresholds |
| MC-803 | Done | Device Monitor Points tab: table, add / edit drawer with type-specific fields (expected HTTP status; database engine and port, the login stays on the device), delete. Device Settings tab: thresholds form (read-only without `devices.configure`), target vs applied version with status (applied / waiting / rejected with reason), licence state. Settings > Monitoring: default thresholds |
| Agent | Done (branch) | `ICloudConfigApplier`: the document is validated, the critical levels go to the agent's monitors and the document is kept in `cloud-config.json`. An invalid document answers `success=false` with the reason. Warning levels, durations and clear levels wait for AG-8 |

### Test results (2026-10-08)

| Suite | Count | Result |
|---|---|---|
| Backend unit (12 new: configuration versions, ack, defaults, point validation per type, document rules) | 343 | passed |
| Architecture | 253 | passed |
| Gateway (5 new: test 9 and the M8 acceptance) | 52 | passed |
| Integration (9 new: configuration, If-Match, version 0, validation, permissions, monitor point CRUD, Starter refused, tenant defaults) | 191 | passed |
| Contract, fake | 16 | passed |
| Portal unit (6 new) | 149 | passed |
| Portal e2e (2 new: threshold saved with a new version, monitor point added and removed) | 26 | passed (the configuration flow after the fix below) |
| Agent (`cloud/m7-connector`) | 105 | passed, 2 skipped |

Coverage: Domain 98.4% line, Application 92.5% line / 77.6% branch, backend 95.8% line; portal 86.9% statements.

The e2e run found one bug, now fixed. Saving the configuration of a device that had none (`If-Match: "0"`) answered
409, because the default configuration was created before the version check.

### Acceptance

- Gateway test 9: an update reaches the agent and its `ConfigApplied` is recorded. The applied version is visible.
  An offline device gets the version after its next `Welcome`.
- Lowering the CPU threshold in the portal makes the simulated agent raise the `cpu` alert at 50 %. Below the
  clear level it clears the alert.
- A document the agent rejects keeps the previous version active, and the reason is shown on the device Settings
  tab.
- `If-Match` conflict: 409 `CONCURRENCY_CONFLICT`.
- A Starter tenant cannot edit monitor points: 403 `FEATURE_NOT_ENTITLED`.

### Notes for the product owner

ADR 0008 lists seven configuration details. The visible one: after the first edit in the portal, the device's
monitor points belong to the cloud, and renaming them on the device no longer changes the portal.

## M9 - Reports, archive and settings (done 2026-10-10)

### Tasks

| Id | Status | Notes |
|---|---|---|
| MC-901 | Done | Reports module: the eight report types (`GET /reports/types` with plan entitlement), `POST /reports` (202, generated by `ReportsJob` every 5 s), `GET /reports`, `GET /reports/{id}/download`, `GET /platform/reports/{type}` (cross-customer, Customer column). CSV always (UTF-8 BOM, formula-like text neutralised); PDF from print-ready HTML through the host's headless Edge/Chrome/Chromium, otherwise `pdfAvailable: false` and 400 `REPORT_PDF_UNAVAILABLE`. Advanced types need `reports.advanced`. A location-restricted user gets reports of their locations only and sees only their own reports (ADR 0009) |
| MC-902 | Done | Media storage under `{Storage:Root}/media/{tenant}/{id}`, paths from the row only and checked to stay inside the folder. Upload validation: allow-list of types (415 `UPLOAD_TYPE_NOT_ALLOWED`), 10 MB (413 `UPLOAD_TOO_LARGE`), empty file and file names without a file part (400), path segments of client names dropped. Downloads are authorised through the owning report or archive item |
| MC-903 | Done | Archive: profile, contacts (one primary), notes, files (multipart), remote access. Internal notes and files are only for `archive.internal` (platform roles). Remote-access values are encrypted with Data Protection, masked in lists, and every reveal is audited at once |
| MC-904 | Done | Settings: Locations tab (links to each location's settings), Integrations tab (webhook URL, signing secret stored encrypted, Test button; delivery only with `notifications.webhook`). Webhooks are signed `sha256=HMAC("{timestamp}.{body}")` with `X-Monitor-Timestamp`. Platform Settings (`/platform/settings`): the offline delay is the default for customers without their own, the retention days drive the telemetry clean-up, the sender is used by the e-mail sender |
| MC-905 | Done | Portal: Reports (customer, location tab, device tab: three columns, recent reports with download and live status), Platform Reports (summary, table, CSV export), Customer Archive (5 tabs, Remote Access for platform roles only), location Archive and Settings tabs, Platform Archive (opens the workspace with a reason), Platform Settings, Audit Log (customer and platform), top-bar search (customers on the platform, devices in a customer). No "coming soon" screen is left |
| MC-906 | Done | Demo seed part 5: Acme gets 4 finished reports generated by the real job, a company profile, 3 contacts, 4 files (PDF, PNG, TXT, CSV), 2 notes (1 internal) and 2 remote-access entries |

### Test results (2026-10-10)

| Suite | Count | Result |
|---|---|---|
| Backend unit (31 new: file names and upload rules, report lifecycle, archive items, platform settings, storage adapters, webhook signing, 413/415 kinds) | 374 | passed |
| Architecture (module isolation of the new Reports, Archive and Media modules; every error code translated) | 253 | passed |
| Gateway | 52 | passed |
| Integration (41 new: report numbers vs independent SQL over the seed for all 8 types, report scope and downloads, CSV injection, archive internal visibility, upload validation, remote access reveal audit, cross-tenant archive with the feature, integrations, platform settings, seed part 5, sign-in audit actor) | 232 | passed |
| Contract, fake | 16 | passed |
| Portal unit (13 new) | 162 | passed |
| Portal e2e (6 new: every menu item, location and device tab opens a working screen; CSV report download; archive notes and files; platform reveal; top-bar search) | 32 | passed |

Coverage: Domain 98.0% line, Application 94.7% line / 82.3% branch, backend 96.2% line; portal 81.3% statements.

Bugs found and fixed: after a reload, platform users on any `/admin/...` page were sent to the dashboard (area
guard ran before the session restore); sign-in audit records named the actor "User" instead of the person.

### Acceptance

- Each report's numbers equal independent SQL over the seed (`ReportNumbersTests`, all eight types).
- A tenant role never receives internal archive items (`Tenant_roles_never_receive_internal_items`).
- File upload validation tests pass (type, size, empty, path traversal in names).
- Every menu item of `00-menu-structure.png` opens a working screen (e2e, platform and customer menus, location and
  device tabs).

### Notes for the product owner

ADR 0009 lists the details decided in M9. Visible ones: PDF uses the browser installed on the server; the location
Archive tab shows only that location's remote access and the files; the Audit Log opens from the Users screens
(the menus have no Audit item). The heaviest platform report (performance, 90 days, 2,373 devices) takes about
4 s; it is on the M10 performance list. The platform name is stored but not shown in the portal yet.
