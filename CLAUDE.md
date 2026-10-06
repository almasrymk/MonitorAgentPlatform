# Monitor Cloud - working rules for Claude Code

You are building **Monitor Cloud**, the multi-tenant SaaS platform behind the Monitor Agent desktop/service
product. The full specification is in `docs/plan/`. Read `docs/plan/00-README.md` first, then the file for
the milestone you are working on. Do not start coding before reading both.

## Non-negotiables

1. **Follow the plan.** If the plan is wrong or incomplete, stop, write an ADR in `docs/adr/` that states the
   problem and the proposed change, and ask before continuing. Never silently deviate.
2. **Follow the design.** The screens in `docs/design/` and the tokens in `docs/design/tokens.scss` (copied
   to `portal/src/styles/_tokens.scss`) are the source of truth for layout, colour and wording. Do not
   restyle, re-order or rename UI elements. `docs/plan/07-frontend-and-design.md` lists the few places where
   the plan deliberately corrects a mockup.
3. **One milestone at a time**, in the order of `docs/plan/10-milestones.md`. A milestone is done only when
   every acceptance criterion passes and `docs/PROGRESS.md` is updated.
4. **Tests are part of the task.** No handler, endpoint, aggregate or component is complete without the tests
   listed in `docs/plan/09-testing.md`. Run the full suite before you report a milestone as done.
5. **Tenant isolation is never optional.** Every tenant-owned entity implements `ITenantOwned`; the tenant
   comes from the token only; every new endpoint gets a cross-tenant test.
6. **No secrets in the repository.** Development secrets live in `appsettings.Development.json` with obvious
   `DEV-ONLY-` values; production values come from environment variables. `gitleaks` runs in CI.
7. **No real-world data in seeds or tests.** Use the sample names and reserved IP ranges of
   `docs/plan/08-seed-data.md`.

## Stack (fixed)

| Area | Choice |
|---|---|
| Backend | .NET 10, ASP.NET Core Web API (controllers), EF Core 10, SQL Server |
| Patterns | Modular monolith, Clean Architecture, DDD, CQRS with MediatR **12.5.0** (pinned), FluentValidation |
| Auth | JWT bearer (HS256 access token 15 min + rotating refresh token); separate device tokens |
| Devices | gRPC bidirectional streaming + Protobuf (`proto/monitor/agent/v1/agent.proto`) |
| Realtime UI | SignalR |
| Frontend | Angular (standalone components, signals), SCSS design tokens, Apache ECharts |
| Tests | xUnit, NetArchTest, WebApplicationFactory on real SQL Server, Playwright |

Do not add: AutoMapper, FluentAssertions 8+, MediatR 13+, a UI component library, Redis, RabbitMQ,
PostgreSQL. Each would need an ADR approved by the product owner.

## Commands

```bash
dotnet build MonitorCloud.slnx -c Release -warnaserror
dotnet test MonitorCloud.slnx -c Release                 # needs SQL Server, see docs/plan/09-testing.md
dotnet run --project src/MonitorCloud.Api --launch-profile http
dotnet ef migrations add <Name> -p src/MonitorCloud.Infrastructure -s src/MonitorCloud.Api
cd portal && npm ci && npm start                           # http://localhost:4300
cd portal && npm test -- --watch=false && npm run e2e
dotnet run --project tools/MonitorCloud.DeviceSimulator -- run --devices 50
```

## Coding conventions

- C#: file-scoped namespaces, `sealed` by default, nullable enabled, warnings as errors, no public setters
  on domain entities, ids are `Guid` v7 created in the domain, time comes from `TimeProvider`.
- Handlers return `Result` / `Result<T>`; exceptions are for bugs. Errors carry a stable `code`
  (`AREA_REASON`, e.g. `DEVICE_NOT_FOUND`).
- Controllers contain no logic: bind, `ISender.Send`, map `Result` to HTTP.
- Queries never load aggregates: project to DTOs with `AsNoTracking` or Dapper.
- Angular: standalone components, `ChangeDetectionStrategy.OnPush`, signals for state, no `any`,
  logical CSS properties only (`margin-inline-start`, never `margin-left`) so RTL works.
- Commits: Conventional Commits, one logical change each, tests in the same commit.

## When you finish a milestone

1. Run backend tests, frontend unit tests and e2e tests; paste the summary into `docs/PROGRESS.md`.
2. List anything skipped or stubbed, with the reason.
3. Stop and wait for review. Do not start the next milestone on your own.
