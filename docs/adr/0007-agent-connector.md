# ADR 0007 - Monitor Agent connector: scope and storage (M7)

- Status: Proposed (applied in M7, awaiting product-owner review)
- Date: 2026-10-08

## Context

05 section 10 lists the agent work package AG-0 to AG-12 for the `UBGMonitor` repository. That repository also
runs its own security hardening plan, `docs/security/SECURITY_PLAN.md`, carried out by another agent. Its
`AGENTS.md` says that an overlap between plans must be reported, not guessed. Several AG items change the same
code as its open runs:

| AG item | Overlapping security run |
|---|---|
| AG-1 secrets | Run 1 |
| AG-7 licensing | Run 5, which waits for decisions D1-D3 |
| AG-9 Local API | Run 2 |
| AG-11 installers | Run 3 |

## Decisions

1. **The connector is a separate project, `MonitorAgent.Cloud`.** It reads the agent only through
   `ICloudAgentSource`, which the service implements over its existing collectors. It does not change the
   monitors, the local API, the licence service or the installers. It is off unless `Cloud:Enabled` or
   `MONITORAGENT_CLOUDURL` is set.
2. **AG-7, AG-9 and the installer half of AG-11 are deferred to the agent's security runs.** The connector
   delivers cloud licence tokens through `ICloudLicenseSink`, so Run 5 can connect `LicenseService` to them.
   AG-1 was already done by the agent's security cleanup.
3. **AG-8 (a single threshold evaluator fed by the cloud document) moves to M8**, together with the central
   configuration that provides the document. Until then the agent's existing thresholds raise the issues.
4. **The outbox is its own SQLite file, `cloud.db`, next to `cloud.json`.** The plan says "in the existing SQLite
   database". A separate file keeps the connector's writes away from the agent's local database, which the
   security plan covers. The table is the one of AG-5, and the row number is the protocol sequence. The same
   file holds the connector's small state: the issues already reported, inventory hashes and the last
   acknowledged sequence.
5. **The agent work stays on the branch `cloud/m7-connector`** and is not merged into `master`. Pushing the
   branch needs the owner's permission.

## Consequences

- M7 is accepted on Windows. Linux is checked by the agent's CI after the push.
- The deferred AG items stay open in `docs/PROGRESS.md` until the related security runs are done.
