# ADR 0002 - Authorization attribute set and the scope of the entitlement check

- Status: Proposed (implemented in M0, awaiting product-owner review)
- Date: 2026-10-06

## Context

`01-architecture.md` section 3 names three request attributes (`[RequirePermission]`, `[PlatformOnly]`,
`[AllowDevice]`) and an architecture test that fails for a request without one. Some requests in the plan
fit none of them:

- sign-in, refresh, logout, accepting an invitation and device enrollment are anonymous (`06-api.md`);
- `GET /auth/me` and the profile endpoints need a signed-in user but no permission;
- background jobs send commands that no user may send.

The `EntitlementBehavior` row says that feature-gated requests are checked and that commands are refused when
the subscription has expired. It does not say whether *every* command is refused after expiry.

## Decision

1. Three more declarative attributes complete the set: `[AllowAnonymousRequest]`, `[AllowAuthenticatedUser]`
   and `[SystemOnly]`. The architecture test requires exactly one of the six (or permissions combined with
   `[PlatformOnly]`).
2. A request with `[RequirePermission]` and without `[PlatformOnly]` needs a tenant scope; a platform user
   without `X-Tenant-Id` gets `TENANT_SCOPE_REQUIRED` (03 section 3).
3. `ENTITLEMENT_EXPIRED` is returned only for commands that carry `[RequiresFeature]`. Commands that are not
   feature-gated (for example user management) keep working after expiry so a customer can still administer
   its account.

## Consequences

- No behaviour of the plan is removed; the additions only make implicit cases explicit.
- If the product owner wants every command blocked after expiry, change point 3 in `EntitlementBehavior` and
  its unit tests.
