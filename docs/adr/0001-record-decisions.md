# ADR 0001 - Record the decisions of the implementation plan

- Status: Accepted
- Date: 2026-10-06

## Context

The implementation plan (`docs/plan/00-README.md` section 4) lists decisions D1-D19. Some were stated by the
product owner, others were chosen while writing the plan to remove ambiguity. They need one place where a
later change can be traced.

## Decision

The following decisions are in force. A change to any of them needs a new ADR that supersedes the row.

| # | Decision | Source |
|---|---|---|
| D1 | Backend .NET 10 Web API, frontend Angular, database SQL Server only (no Redis, no broker, no time-series database). | Owner |
| D2 | Modular monolith, Clean Architecture, DDD, CQRS through MediatR. | Owner |
| D3 | MediatR pinned to 12.5.0 (last Apache-2.0 release). Moving to 13+ is an owner decision. | Plan |
| D4 | JWT authentication for users; a separate JWT type for devices. | Owner |
| D5 | Multi-tenant by `TenantId` on every tenant-owned row, EF Core global query filters and write guards. | Owner + Plan |
| D6 | Layer projects (`SharedKernel`, `Domain`, `Application`, `Infrastructure`, `Api`) with one folder and one schema per module; boundaries enforced by architecture tests. | Plan |
| D7 | A Monitor Cloud tenant is a customer organisation, mapped 1:1 to a Licensing Platform customer. | Plan |
| D8 | The word is **Location** (not Site). Device groups are out of scope for now. | Plan |
| D9 | Customer roles: Administrator, IT Manager, Technician, Report Viewer. Platform roles: Platform Admin, Platform Support. | Plan |
| D10 | Enrollment = activation: Monitor Cloud activates the seat with its own API client; the agent holds no Licensing credentials. | Plan |
| D11 | Device identity is a 256-bit secret issued at enrollment, stored hashed, exchanged for a 60-minute device JWT. | Plan |
| D12 | Thresholds and monitor points are evaluated on the agent; the cloud evaluates offline and licence state only. | Plan |
| D13 | First release has alerts, not the incident workflow. "Incident Trend" shows alerts opened per day. | Plan |
| D14 | Health score = healthy devices / total devices. Offline devices have health `Unknown` and are not healthy. | Plan |
| D15 | Telemetry history is one wide row per device per minute in SQL Server via `SqlBulkCopy`; live values in memory and over SignalR. | Plan |
| D16 | Platform staff enter a workspace with `X-Tenant-Id` (platform roles only); opening it is audited with a reason. | Plan |
| D17 | Seed data uses invented company names shaped like the designs. | Plan |
| D18 | UI languages English (default) and Arabic with RTL; logical CSS properties only from the first commit. | Plan |
| D19 | Unlicensed devices keep basic reporting for a 14-day grace period with locked features; afterwards telemetry is dropped. | Plan |

## Consequences

- Architecture tests (`tests/MonitorCloud.ArchitectureTests`) enforce D2, D3, D5 and D6.
- `portal/scripts/check-logical-css.mjs` enforces D18 in the portal lint step.
- Later ADRs reference these ids.
