# ADR 0003 - Grouping of request types in Application

- Status: Proposed (applied from M1, awaiting product-owner review)
- Date: 2026-10-06

## Context

`01-architecture.md` section 3 suggests `Application/<Module>/Commands/<Name>/{Command, Handler, Validator}.cs`.
M1 alone has more than 40 requests. Three files per request scatter small, tightly coupled code (a record, a
ten-line validator and a handler) and make reviews slower.

## Decision

Requests of one feature live in one file per area inside the module folder, each request followed by its
validator and handler (for example `Application/Tenancy/LocationRequests.cs`,
`Application/Identity/Users/TenantUserRequests.cs`). Larger requests (sign-in) keep the folder layout.
Everything the plan checks still holds and is tested: one handler per request, a validator per command, an
authorization attribute on every request, module isolation.

## Consequences

- Fewer, cohesive files; the architecture tests are unchanged.
- If the product owner prefers the folder layout, the split is mechanical (no code changes).
