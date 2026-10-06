# 09 - Testing

Testing is part of every task. A milestone cannot be accepted with a failing, skipped or missing required
test. Tests must be deterministic: no `Thread.Sleep`, no dependence on wall-clock time (use `TimeProvider`
and a test clock), no shared state between tests, no network except the local SQL Server.

## 1. Test projects

| Project | Kind | Runs against | Contains |
|---|---|---|---|
| `MonitorCloud.UnitTests` | unit | nothing external | Domain aggregates and services, validators, handlers with in-memory fakes of ports, pipeline behaviours, mappers, generators |
| `MonitorCloud.ArchitectureTests` | architecture | assemblies | NetArchTest rules of 01 section 2 and the conventions listed below |
| `MonitorCloud.IntegrationTests` | integration | the real API host + **SQL Server** | Every endpoint: happy path, validation, authorization, tenant isolation, concurrency; background jobs; migrations; seed |
| `MonitorCloud.GatewayTests` | integration | in-memory gRPC `TestServer` + SQL Server | Enrollment, token, stream lifecycle, ingestion, presence, live mode, config sync, using `SimulatedAgent` |
| `MonitorCloud.ContractTests` | contract | `FakeLicensingGateway`, and the live Licensing Platform when `LICENSING_CONTRACT_URL` is set | One suite of behaviours executed against both adapters |
| `portal` unit tests | unit | browser test runner | Components, stores, pipes, guards, interceptors |
| `portal/e2e` | end-to-end | built portal + API with demo seed | Playwright flows and screenshot baselines |

Libraries: xUnit, NSubstitute, Shouldly, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.MsSql`,
NetArchTest.Rules, `Grpc.Net.Client` over the test server handler, Playwright. (No FluentAssertions, no
AutoFixture magic for domain objects: use explicit test-data builders in `tests/Shared/Builders`.)

### SQL Server for tests

- Default: `Testcontainers.MsSql` starts one SQL Server container per test run (collection fixture); each
  test class gets its own database created from migrations once; between tests the data is cleared by a
  `DELETE` script generated from the EF model in dependency order (`TestDatabase.ResetAsync`).
- Without Docker: set `MONITOR_TEST_SQLSERVER="Server=.;Trusted_Connection=True;TrustServerCertificate=True"`
  to use a local instance; databases are named `MonitorCloud_Test_{guid}` and dropped at the end.
- SQLite and the EF in-memory provider are **not** used: bulk copy, filtered unique indexes and row versions
  must be exercised for real.

## 2. Required unit tests

**Domain** (one test class per aggregate; every public behaviour, every rule, every state transition):

- `Tenant`: create, suspend/resume/archive transitions and their invalid combinations, events raised.
- `Location`: default location cannot be deleted or closed; code uniqueness is validated by the handler.
- `LocationEnrollmentCode`: expiry, max uses, revoke, hash comparison.
- `User`: e-mail normalisation, password rules, lock-out after 5 failures and unlock after 15 minutes
  (test clock), last-administrator rule, invitation acceptance, location scope.
- `Device`: enroll, rename, move, retire, re-activate; events.
- `Alert`: raise, touch (occurrences, severity change), acknowledge, resolve, re-open creates a new alert.
- `DeviceHealthCalculator`: full truth table (connection x open alert severities x licence state x grace).
- `MonitorPoint`: validation per type; `DeviceConfiguration`: version increments, schema validation.
- `TenantEntitlement` derivation (04 section 4.3): one test per row of the rule table, plus no
  subscriptions, several licences, unlimited licence.
- Value objects: equality and validation.

**Application** (handlers with faked ports):

- Each command: success, each validation failure, each domain failure, not-found, permission denied,
  feature not entitled, and that the expected domain event and audit record are produced.
- Each query: mapping, paging, sorting, filters.
- Behaviours: authorization (each attribute), validation, entitlement, unit of work (rolls back on failure),
  caching (key contains tenant and location scope; not shared between tenants).
- `EnrollDeviceCommand`: new device, same fingerprint re-enroll (old secret revoked), retired device
  re-activated, unknown location code, tenant suspended (seat released again), Licensing unavailable,
  each `LIC_*` code passed through, auto-provisioned tenant.
- `ApplyIssueEventCommand`: raised/severity-changed/cleared, duplicates, backlog older than 15 minutes
  (no notification), unknown device.
- `LicenseRefreshJob`, `LicensingSyncJob` (cursor advance, no event when nothing changed, failure counting),
  `PresenceMonitor` (missed heartbeats, grace period, offline alert after 2 minutes, location-wide outage),
  `MetricRollupJob` (weighted average, max, idempotent re-run), retention.

Coverage gates (Coverlet, enforced in CI): `Domain` >= 90% line, `Application` >= 85% line and >= 75%
branch, whole backend >= 80% line. Generated code and migrations are excluded.

## 3. Required architecture tests

1. Layer rules 1-5 of 01 section 2.
2. Module isolation (rule 6) for every pair of modules.
3. No public setters on domain entities; all aggregates have private parameterless constructors.
4. Every type with a `TenantId` property implements `ITenantOwned`; every `ITenantOwned` entity has a query
   filter (inspect the EF model).
5. Every MediatR request has exactly one handler; every command has a validator; every request has an
   authorization attribute.
6. Controllers reference only `ISender`, DTOs and ASP.NET Core types.
7. `IgnoreQueryFilters` appears only in the allow-listed types (03 section 4).
8. No reference to forbidden packages (AutoMapper, FluentAssertions, MediatR >= 13).
9. `Application` handlers do not call `DateTime.Now/UtcNow` or `Guid.NewGuid()` (use `TimeProvider`, v7 ids).
10. `dotnet ef migrations has-pending-model-changes` returns no pending changes.

## 4. Required integration tests (API)

**Generic suites driven by the endpoint list** (reflection over `EndpointDataSource`, so new endpoints are
covered automatically; a new endpoint without metadata fails the suite):

| Suite | Asserts |
|---|---|
| `AnonymousAccessTests` | Every endpoint except the allow-list (`login`, `refresh`, `logout`, `invitations/accept`, agent `enroll`/`token`, health, OpenAPI) returns 401 without a token |
| `PermissionMatrixTests` | For every endpoint and every role: allowed roles get a non-403 answer, others get 403 `AUTH_FORBIDDEN`. Expected values come from the matrix in 03, written as test data |
| `CrossTenantTests` | Tenant B calling every tenant endpoint with ids that belong to tenant A gets 404 (never 403, never data); list endpoints never contain tenant A rows; writes with tenant A ids fail and leave tenant A unchanged |
| `LocationScopeTests` | A user restricted to one location sees only its locations, devices, alerts and notifications on every endpoint |
| `PlatformScopeTests` | Tenant users get 403 on `/platform/**`; platform users without `X-Tenant-Id` get `TENANT_SCOPE_REQUIRED` on tenant endpoints; with it they see that tenant only; a tenant user sending `X-Tenant-Id` of another tenant is rejected and audited |
| `DeviceTokenTests` | A device token is rejected by every REST endpoint; a user token is rejected by the gateway |
| `ProblemDetailsTests` | Every error has `application/problem+json`, `code`, `traceId` |
| `PagingAndSortingTests` | Every list endpoint honours `page`/`pageSize`, caps at 200, rejects unknown `sort` |

**Per feature:** auth (login, lock-out, refresh rotation, re-use revokes the family, logout, suspended
tenant), users (invite, accept, role change, last administrator), tenants (create, suspend blocks sign-in
and refresh, archive disconnects devices), locations (CRUD, delete rules, enrollment codes shown once),
devices (list filters and sorts on seeded data, rename, move, retire releases the seat, unlicense),
alerts (list, acknowledge, resolve, not allowed for self-resolving alerts), notifications (feed, unread
count, mark read), subscription (matches the fake Licensing data), configuration (`If-Match`, version bump,
feature gate), reports (generate, download, wrong tenant), archive (internal items hidden from tenant roles,
reveal is audited), dashboards (numbers equal independent SQL counts over the seed), workspace sessions
(reason required, audited), concurrency (two updates with the same row version -> one 409).

**Data and jobs:** migrations apply to an empty database and are idempotent; `DemoSeeder` runs twice without
duplicates and its totals satisfy the invariants of 08; outbox dispatch with retry and dead-letter; inbox
prevents double handling; `TelemetryWriter` idempotency (same batch twice -> same rows); retention deletes
only old rows.

## 5. Required gateway tests

Using `SimulatedAgent` against the in-memory server:

1. Enroll -> token -> connect -> `Welcome`; device becomes `Online`; `deviceStateChanged` is broadcast.
2. First message not `Hello`, unsupported protocol version, expired token, revoked credential, retired
   device, archived tenant: each closes the stream with the documented code.
3. Second connection replaces the first (`DUPLICATE_SESSION`); three replacements raise the clone alert.
4. `MetricBatch` is stored, `DeviceStates` updated, `Ack` sent after commit; the same batch resent is
   acknowledged and not duplicated; a batch sent while the database is down is acknowledged only after it
   recovers.
5. Backlog: 3 hours of minutes in chunks are accepted in order; minutes beyond retention are acknowledged
   and dropped.
6. `IssueEvent` raised -> alert, health, tile counts, notification, SignalR events; cleared -> resolved.
7. Heartbeats stop -> `Offline` after 3 intervals; offline alert after 2 minutes; reconnect resolves it.
   `Goodbye(HOST_SHUTTING_DOWN)` -> `Info` severity.
8. Live mode: `live-sessions` -> agent receives `SetTelemetryMode(LIVE)`; samples reach the SignalR group and
   are not stored; mode ends after the TTL.
9. Config: update in the API -> `ConfigUpdate` on the stream -> `ConfigApplied` -> applied version visible;
   an offline device receives it on its next `Welcome`.
10. Licence: refresh job pushes `LicenseUpdate`; a rejected refresh makes the device `Unlicensed`; behaviour
    during and after the grace period (00 D19).
11. Tenant isolation: a device token for tenant A can never write rows for tenant B (forged ids in payloads
    are ignored).
12. Back-pressure: with the writer paused, the gateway stops reading and memory stays bounded.

## 6. Contract tests (Licensing)

The suite `LicensingGatewayContract` runs against `FakeLicensingGateway` always, and against
`HttpLicensingGateway` when `LICENSING_CONTRACT_URL`, `..._CLIENT_ID`, `..._CLIENT_SECRET` and a seeded
product key are provided (CI job `licensing-contract`, nightly and on demand):

activate (new, repeated, limit reached, invalid key, suspended, expired), refresh, release then activate
again, entitlements shape, plans, change feed ordering and cursor, signing keys verify the returned token,
`customerId`/`licenseId` present (LP-2), rate-limit answer mapped to `RATE_LIMITED`.

## 7. Frontend tests

- **Unit:** every `shared/ui` and chart component (inputs -> DOM, states, `goodWhen` colours, RTL class),
  stores, pipes, guards (permission and area), interceptors (token attach, single refresh on 401 with
  queued requests, `X-Tenant-Id` only inside a workspace, problem-details mapping), `LiveService`
  (subscribe/unsubscribe, reconnect). Gate: >= 75% statements.
- **e2e (Playwright, against the demo seed):**
  1. Platform admin signs in, sees the dashboard, opens Customers, filters by plan, opens a workspace with a
     reason, sees the banner, drills to a location, the device list and a device.
  2. Device list updates when the simulator changes a device (card value changes without reload).
  3. Device screen shows live values and returns to stored values when the device goes offline.
  4. Customer administrator: dashboard, locations, add location, generate an enrollment code, invite a user,
     change alert settings, acknowledge an alert.
  5. Report Viewer: cannot see Users/Settings, direct URL is blocked.
  6. Suspended customer cannot sign in.
  7. Arabic: switch language, layout is RTL, key screens render.
  8. Accessibility: axe checks on the seven main screens with no serious violations.
- **Screenshot baselines** for the seven designed screens at 1672 x 941, dark theme, with the simulator off
  and animations disabled. A reviewer compares each baseline with its design image once; afterwards CI
  guards against unintended change (threshold 0.2%).

## 8. Performance and load tests

Run on demand and before release (`tools/MonitorCloud.DeviceSimulator load`, k6 scripts in `tests/load`),
on a machine comparable to production, with the demo seed.

| Target | Value |
|---|---|
| Device list (`GET /devices`, 24 items, any filter) | p95 < 150 ms |
| Dashboard composites (uncached) | p95 < 300 ms |
| Device metrics, 7 days | p95 < 300 ms |
| Sign-in | p95 < 400 ms |
| Ingestion | 1,500 simulated devices for 10 minutes: no data loss, acknowledgement p95 < 5 s, API latency targets still met |
| Live update | change on the device visible in the browser within 5 s (p95) |
| Offline detection | within 2 minutes |
| Memory | gateway process stable (no growth) over the 10-minute run |
| Portal | initial bundle within budget; Lighthouse performance >= 85 on the dashboard |

Results are written to `docs/perf/{date}.md`. A missed target blocks the release milestone.

## 9. Security tests

JWT tampering (changed role, tenant, expired, wrong audience, `alg=none`), refresh-token re-use, password
reset tokens single-use, lock-out, rate limits on login/enroll/token, mass-assignment (extra `tenantId`,
`role`, `status` fields in bodies are ignored), IDOR on every id parameter (covered by `CrossTenantTests`),
upload validation (type, size, path traversal in file names), SQL injection probes on search and sort
parameters, secrets never present in logs (log sink inspected in tests after sign-in, enrollment and token
exchange), security headers present, CORS limited to configured origins.

## 10. CI (`.github/workflows/ci.yml`)

| Job | Steps |
|---|---|
| `backend` | restore, vulnerable-package gate (High/Critical fails), build with warnings as errors, unit + architecture tests, integration + gateway + contract(fake) tests with the SQL Server service container, coverage gates, pending-migration check |
| `portal` | `npm ci`, lint, `api:types` freshness check, unit tests with coverage gate, production build with budgets |
| `e2e` | start SQL Server, API with demo seed and built portal, run Playwright, upload traces and screenshots |
| `secrets-scan` | gitleaks on full history |
| `licensing-contract` | nightly / manual, live contract suite |

A pull request cannot merge unless `backend`, `portal`, `e2e` and `secrets-scan` are green.
