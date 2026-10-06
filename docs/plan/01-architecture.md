# 01 - Architecture

## 1. Solution layout

```
MonitorCloud.slnx
Directory.Build.props            net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors=true
Directory.Packages.props         central package versions (exact versions, no ranges)
.editorconfig  .gitignore  .gitleaks.toml
proto/monitor/agent/v1/agent.proto
src/
  MonitorCloud.SharedKernel/     Entity, AggregateRoot, ValueObject, Result, Error, DomainEvent, Guard, ITenantOwned
  MonitorCloud.Domain/           one folder per module, no framework references
  MonitorCloud.Application/      one folder per module: Commands, Queries, DTOs, validators, event handlers, ports
  MonitorCloud.Infrastructure/   EF Core, SQL, Licensing HTTP client, e-mail, file store, background jobs, seed
  MonitorCloud.AgentProtocol/    generated gRPC/Protobuf types (targets net8.0;net10.0 so the agent can use it)
  MonitorCloud.AgentGateway/     gRPC service, session registry, ingestion pipeline entry
  MonitorCloud.Api/              host: controllers, SignalR hub, auth, middleware, composition root
tests/
  MonitorCloud.UnitTests/
  MonitorCloud.ArchitectureTests/
  MonitorCloud.IntegrationTests/
  MonitorCloud.GatewayTests/
  MonitorCloud.ContractTests/
tools/
  MonitorCloud.DeviceSimulator/
  MonitorCloud.LicensingSeeder/
portal/                          Angular application
seed/                            generated demo files (licensing-fake.json, README.md); *.local.json is git-ignored
docs/                            plan, design, adr, PROGRESS.md, operations.md
.github/workflows/ci.yml
```

`MonitorCloud.Api` hosts REST, SignalR and the gRPC gateway in one process. `MonitorCloud.AgentGateway` is a
separate project so it can later run as its own host without touching the modules.

## 2. Modules

Each module is a folder with the same name in `Domain`, `Application` and `Infrastructure`, and one SQL
schema.

| Module | Schema | Responsibility |
|---|---|---|
| Identity | `identity` | Users, roles, permissions, refresh tokens, sign-in |
| Tenancy | `tenancy` | Tenants (customers), locations, enrollment codes, suspend/archive |
| Licensing | `licensing` | Entitlement cache, device licence state, sync with the Licensing Platform |
| Devices | `devices` | Devices, credentials, inventory, current state read model |
| Telemetry | `telemetry` | Metric history, roll-ups, retention, live snapshot |
| Monitoring | `monitoring` | Monitor points, their state, alerts, alert statistics |
| Notifications | `notifications` | In-app notifications, channel settings, recipients, e-mail delivery |
| Configuration | `config` | Versioned device configuration and acknowledgements |
| Reports | `reports` | Report definitions, generation, generated files |
| Archive | `archive` | Customer records: profile, contacts, notes, files, remote-access entries |
| Audit | `audit` | Audit records |
| Messaging | `messaging` | Outbox and inbox |
| Media | `media` | Uploaded files metadata |

### Dependency rules (enforced by `MonitorCloud.ArchitectureTests`)

1. `SharedKernel` references nothing of ours.
2. `Domain` references only `SharedKernel`; no EF Core, ASP.NET Core, MediatR, `System.Net.Http`.
3. `Application` references `Domain` and `SharedKernel`; not `Infrastructure`, `Api`, `AgentGateway`, EF Core
   provider packages. It may reference `Microsoft.EntityFrameworkCore` abstractions only through the
   `IAppDbContext` port.
4. `Infrastructure` and `AgentGateway` do not reference `Api`.
5. Controllers and hubs depend only on `MediatR.ISender`, DTOs and ASP.NET Core.
6. **Module isolation:** a type in `Domain.<A>` must not reference a type in `Domain.<B>`. Other modules are
   referenced by id (`Guid`) only. In `Application`, module A may use module B only through
   `Application.<B>.Contracts` (interfaces and DTOs) or through integration events. `Messaging`, `Audit` and
   `SharedKernel` types are exempt.
7. Domain entities have no public setters. Every entity with a `TenantId` implements `ITenantOwned`.
8. Every MediatR request has exactly one handler, and every command has a validator.

## 3. Layers in practice

```
HTTP / gRPC / SignalR  ->  Controller or gRPC service        (Api, AgentGateway)
                           maps transport <-> request, no logic
                       ->  ISender.Send(command | query)
                           pipeline behaviours
                       ->  Handler                            (Application)
                           commands: load aggregate, call domain method, save
                           queries: project to DTO
                       ->  Aggregate / domain service         (Domain)
                       ->  IAppDbContext, ports               (implemented in Infrastructure)
```

### CQRS conventions

- Commands: `public sealed record CreateLocationCommand(...) : ICommand<LocationDto>;`
  Queries: `public sealed record GetDevicesQuery(...) : IQuery<PagedResult<DeviceListItemDto>>;`
  `ICommand<T>` and `IQuery<T>` extend `IRequest<Result<T>>`.
- Folder: `Application/<Module>/Commands/<Name>/{Command, Handler, Validator}.cs` and
  `Application/<Module>/Queries/<Name>/{Query, Handler}.cs`.
- Commands change one aggregate per transaction. Effects on other modules happen through domain events
  written to the outbox in the same transaction and handled asynchronously.
- Query handlers use `IReadDbContext` (no-tracking `IQueryable`s with the same tenant filters) or
  `ISqlConnectionFactory` + Dapper for aggregate-heavy dashboards. They never return entities.
- Requests declare their authorization: `[RequirePermission(Permissions.DevicesRead)]`,
  `[PlatformOnly]`, `[AllowDevice]`. A request without one of these attributes fails an architecture test.

### Pipeline behaviours (registered in this order)

| # | Behaviour | Does |
|---|---|---|
| 1 | `LoggingBehavior` | Request name, tenant, user, duration; warns above 500 ms |
| 2 | `AuthorizationBehavior` | Checks the attribute against `ICurrentUser`; returns `AUTH_FORBIDDEN` |
| 3 | `ValidationBehavior` | Runs FluentValidation; returns `VALIDATION_FAILED` with field errors |
| 4 | `EntitlementBehavior` | For requests marked `[RequiresFeature("...")]`, checks the tenant's plan features (`FEATURE_NOT_ENTITLED`); refuses commands when the subscription is expired (`ENTITLEMENT_EXPIRED`) |
| 5 | `UnitOfWorkBehavior` | Commands only: one `SaveChangesAsync`, writes audit + outbox in the same transaction |
| 6 | `QueryCachingBehavior` | Queries implementing `ICacheableQuery`: `IMemoryCache`, key includes tenant and scope, TTL from the query |

## 4. Shared kernel

Copy the shape of the Licensing Platform's shared kernel so both code bases read the same:
`Entity` (Guid v7 id), `AggregateRoot` (domain events), `ValueObject`, `Result` / `Result<T>`,
`Error(Code, Message, Kind)` with `ErrorKind { Validation, NotFound, Conflict, Forbidden, Unauthorized,
Locked, TooManyRequests, Gone }`, `DomainException(Error)`, `Guard`, `IDomainEvent`, `ITenantOwned`.

## 5. Persistence

- One `AppDbContext` implementing `IAppDbContext` (write side) and `IReadDbContext` (read side).
  `ValueGeneratedNever` for all ids. One `IEntityTypeConfiguration` per entity, placed in
  `Infrastructure/<Module>/Persistence`. Table names singular-plural as in 02.
- Schema per module. Enums stored as strings (`nvarchar(32)`) except in telemetry tables.
- `datetimeoffset(3)` for business timestamps, `datetime2(0)` UTC for telemetry buckets.
- Optimistic concurrency with a `rowversion` column on aggregates edited by users.
- Migrations live in `Infrastructure/Persistence/Migrations`. Development applies them at start-up; other
  environments apply them as a release step (`Seed:ApplyMigrations=false`).
- Tenant isolation, outbox writing and audit are implemented in `SaveChangesAsync` exactly as described in 03.
- The telemetry hot path does not use EF Core change tracking: see section 8.

## 6. Messaging (in-process)

- `messaging.OutboxMessages` is written in the business transaction. `OutboxDispatcher`
  (`BackgroundService`) polls every 2 s, dispatches to `IIntegrationEventHandler<T>` implementations with
  retry (exponential back-off, dead-letter after 8 attempts) and records each handler in
  `messaging.InboxMessages` so it runs once.
- Integration events are versioned records (`DeviceEnrolledV1`, `AlertRaisedV1`, `AlertResolvedV1`,
  `DeviceWentOfflineV1`, `DeviceCameOnlineV1`, `EntitlementChangedV1`, `TenantSuspendedV1`).
- No external broker (D1). The dispatcher interface is the seam for one later.

## 7. Cross-cutting

| Concern | Implementation |
|---|---|
| Errors | RFC 7807 `application/problem+json` with `code` and `traceId` on every non-2xx response |
| Validation | FluentValidation in the pipeline; model-binding errors use the same format |
| Logging | Serilog, structured, `X-Correlation-Id` accepted or created and returned; never log bodies, tokens, keys |
| Health | `/health/live`, `/health/ready` (database, outbox lag, gateway) |
| OpenAPI | `/openapi/v1.json`; the portal's types are generated from it (see 07) |
| Rate limiting | Per IP on `auth/*` and `agent/v1/enroll`; per device on `agent/v1/token`; per user on exports |
| Time | `TimeProvider` everywhere; tests use a controllable clock |
| Background jobs | `BackgroundService`s, each idempotent: outbox, licensing sync, presence monitor, telemetry writer, roll-up, retention, licence refresh, report generation |
| Configuration | Options classes with `ValidateOnStart`; the app refuses to start with an empty signing key |
| Security headers | CSP, `X-Content-Type-Options`, `Referrer-Policy`, HSTS outside Development |
| CORS | Explicit origins from configuration; credentials not used (tokens in headers) |

## 8. Performance design

Targets are in 09. The design choices that make them reachable:

**Ingestion**

- gRPC handlers do no database work. They validate, then write to bounded `Channel<T>`s
  (`BoundedChannelFullMode.Wait`, so a slow database slows the acknowledgements instead of using memory).
- `TelemetryWriter` drains the channel every 2 s or 1,000 rows, whichever comes first, and writes with
  `SqlBulkCopy` into a `#staging` table followed by one `INSERT ... WHERE NOT EXISTS` (idempotent on the
  primary key) and one set-based `UPDATE devices.DeviceStates ... FROM @tvp`.
- Acknowledgements are sent to the agent only after the batch is committed.
- Live samples (device screen open) are never written; they go straight to SignalR.

**Reads**

- List and dashboard queries read `devices.DeviceStates` (one narrow row per device, covering indexes on
  `(TenantId, LocationId)` including the counted columns) instead of joining history.
- Dashboard endpoints are composite (one request per screen) and cached per tenant/scope for 5-10 s.
- Trend charts read `monitoring.AlertDailyStats` and `telemetry.MetricHours`, never raw rows.
- Every list endpoint is paged on the server (`page`, `pageSize` max 200) with a stable sort.
- EF Core: `AsNoTracking`, explicit projections, compiled queries for the five hottest queries,
  `AsSplitQuery` forbidden in list endpoints.
- Response compression (Brotli) for JSON; `System.Text.Json` source generation for hot DTOs.

**Realtime**

- SignalR groups per tenant, location and device. Device state changes are coalesced: at most one
  `summaryChanged` per tenant/location per 2 s.

**Frontend**

- Lazy-loaded feature areas; ECharts loaded only on screens with charts; `OnPush` + signals;
  virtual scrolling above 100 rows; images as WebP with fixed dimensions; initial bundle budget in 07.
