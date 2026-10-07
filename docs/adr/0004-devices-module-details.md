# ADR 0004 - Details of the Devices module (M3)

- Status: Proposed (applied in M3, awaiting product-owner review)
- Date: 2026-10-07

## Context

Building M3 showed five places where the plan does not say enough, or where following it literally would break
the module isolation rule (01 section 2, rule 6) or a requirement elsewhere in the plan.

## Decisions

1. **`DeviceState.UnlicensedSince`.** 02 section 4 lists the columns of `DeviceStates` without a licence date. D19
   needs one: health turns `Unknown` 14 days after a device loses its licence. Every list and dashboard reads
   `DeviceStates`, so the column lives there, next to `LicenseState`. It mirrors `DeviceLicense.UnlicensedSince`.
2. **`DeviceLicense` is an aggregate with `DeviceFingerprint`.** It needs the fingerprint because the Licensing
   Platform identifies a seat by licence id and fingerprint for refresh and release. It raises
   `DeviceLicenseChangedV1`, a domain event in `Domain.Licensing`, so the Devices module can follow licence changes
   without referencing Licensing internals. The handler reads the seat again rather than trusting the event, so an
   older event handled late cannot undo a newer change.
3. **Enrollment-code requests live in `Application/Tenancy`.** `LocationEnrollmentCode` belongs to Tenancy
   (02 section 2). The Devices module redeems a code through `IEnrollmentCodeRedeemer` (Tenancy contract).
   `AgentSettings` is a Devices contract, so Tenancy can build the install commands.
4. **The default location is created when the tenant is provisioned.** It is also created by the existing
   `CreateDefaultLocation` outbox handler, which is idempotent. A device enrolling for a brand-new Licensing customer
   must land in "Unassigned" within the same request (04 section 4.1), so the outbox alone is too late.
5. **One maintenance job.** `DeviceMaintenanceJob` runs every five minutes. It refreshes due device licences
   (`LicenseRefreshJob` of MC-304), applies the D19 grace rule, and deletes enrollment attempts older than 90 days.
   Each step runs in its own scope.

## Consequences

The module-isolation architecture tests pass without exceptions. If the product owner prefers separate jobs, or
the enrollment-code requests under `Application/Devices`, only file locations change.
