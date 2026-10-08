# ADR 0008 - Details of the central configuration (M8)

- Status: Proposed (applied in M8, awaiting product-owner review)
- Date: 2026-10-08

## Context

02 section 8 defines `DeviceConfiguration` with one `DocumentJson`, and 05 section 8 shows the document. Monitor
points also exist as rows (`monitoring.MonitorPoints`, 02 section 6), so the plan describes them in two places.

## Decisions

1. **Monitor points live in the Monitoring module only.** `DeviceConfiguration.DocumentJson` holds telemetry,
   thresholds and features. The document sent to the agent combines that JSON with the device's point
   definitions and the version. A point change bumps the configuration version through
   `IConfigurationVersioning`, so there is one version for both.
2. **The first edit in the portal makes the cloud the owner of a device's points.** All the device's points become
   `Origin = Cloud` (05 section 8: "from then on the cloud version wins"). After that, the agent's
   `MonitorPointReport` only updates statuses: it no longer creates, renames or removes definitions. Before the
   first edit, reports import points as in M6.
3. **The `If-Match` of a configuration is its version number** (`ETag: "12"`), not a row version. Users see and
   compare this number ("applied vs target version"). Monitor points keep their row version.
4. **A device gets its configuration when it is first needed.** Enrollment creates it from the tenant defaults
   (handler of `DeviceEnrolledV1`). Seeded devices and devices enrolled before M8 get theirs on the first GET-and-save
   or point edit, at version 1. Until then the gateway sends no `ConfigUpdate` (target version 0).
5. **`DeviceConfigurationAck` keeps the last applied version and, separately, the last rejected version with its
   reason.** One rejection does not hide the version that is still active on the device.
6. **Secrets of Database points never travel.** `secretRef` is the point key; the login is entered on the device.
7. **The real agent applies the critical levels for now.** Its monitors alert on one critical level per resource.
   Warning levels, durations and clear levels wait for the single evaluator of AG-8 (ADR 0007). The simulator
   evaluates warning, critical and clear levels for the acceptance test.

## Consequences

The configuration API matches 06. Point 2 is visible to users: after the first edit, renaming a point on the device
no longer changes the portal.
