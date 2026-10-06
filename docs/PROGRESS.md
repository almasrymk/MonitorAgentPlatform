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
