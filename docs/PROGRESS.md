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

- CI status on GitHub can't be read from this machine because the `gh` CLI isn't signed in. Check the
  Actions tab after the push.
- Design images `docs/design/00..07-*.png` are needed before M3's screenshot baselines.
