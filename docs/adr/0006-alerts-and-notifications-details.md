# ADR 0006 - Details of alerts and notifications (M6)

- Status: Proposed (applied in M6, awaiting product-owner review)
- Date: 2026-10-08

## Context

02 sections 6, 7 and 12, 05 section 2 and 06 describe the Monitoring and Notifications modules. A few points are
not covered by the plan, or the plan could not be followed literally without breaking another rule of the plan.

## Decisions

1. **The `license` alert does not count for the device health.** 00 D19 says an unlicensed device keeps its health
   during the grace period and becomes `Unknown` after it. If the `license` alert (Warning) counted, every
   unlicensed device would turn Warning at once. `IOpenAlertCounter` therefore skips the `license` key. The alert
   is still listed, and the licence state is shown on its own.
2. **Alert events carry the severity as a name.** `AlertRaisedV1`, `AlertSeverityChangedV1` and
   `AlertResolvedV1` use strings (`"Critical"`, `"Auto"`), not the `AlertSeverity` and `ResolvedBy` enums. Other
   modules may only use the domain events of a module (rule 6, enforced by the architecture tests). The enums
   are not events, so the Devices and Notifications handlers could not read them.
3. **The platform feed has its own unread-count and read endpoints.**
   `GET /platform/notifications/unread-count` and `POST /platform/notifications/read` are added. The plan's
   `/notifications/*` endpoints need a tenant scope (03 section 3), so the platform bell could not use them.
4. **The platform feed holds platform notifications only** (`TenantId` null). Mixing every customer's feed into it
   would leave hundreds of rows unread, because those rows are read by the customer's users. Alerts across
   customers are listed by `GET /platform/alerts` and on the Platform Admin Dashboard.
5. **Cloud issue keys are reserved.** An agent cannot raise these keys; such an `IssueEvent` fails validation and
   is acknowledged and dropped:
   - `device-offline`
   - `license`
   - `clock-skew`
   - `clone-suspected`
6. **The possible-clone report (rule 3) is a `clone-suspected` Warning alert on the device, plus one platform
   notification.** The plan says "platform alert" without defining one. Every alert belongs to a tenant and a
   device (02 section 6).
7. **`DeviceLicense.Renew` always raises `DeviceLicenseChangedV1`.** Gateway test 10 requires the refreshed token
   to reach the connected agent (`LicenseUpdate`). The handlers of the event read the current seat, so an extra
   event is harmless.
8. **`MonitorPointSampler` reads `MonitorPointStates`, not memory.** It runs every minute and writes one sample
   per enabled point with a single `INSERT ... SELECT`. It is idempotent per minute. The states are updated by
   every `MonitorPointReport`, so the result matches an in-memory sampler without a second cache.
9. **Settings > General stores the default language on the tenant** (`Tenant.DefaultLanguage`, `en` or `ar`).
   The offline-alert severity and delay live in `monitoring.MonitoringSettings` (default Critical, 2 minutes).
   `PendingOfflineAlerts` holds devices that went offline until the delay is over.
10. **`NotificationDelivery` has extra columns** for the retry: `Subject`, `Body` and `NextAttemptAt`.
    - Five attempts are made, 1, 2, 4 and 8 minutes apart.
    - The delivery is then `Failed`, with the last error.
    - Webhook delivery is not sent yet: the channel switch and the URL are stored (`notifications.webhook`), and
      sending follows with the integrations of M9.
11. **Location-level notifications are a tab of the location** (`.../locations/:id/notifications`), next to
    Overview and Devices. It reuses the Notifications screen with the location filter.

## Consequences

The API has two extra endpoints (point 3) and one extra column on `tenancy.Tenants` (point 9). There is no change
to the protocol. Points 1 and 4 change what users see, and are listed for review.
