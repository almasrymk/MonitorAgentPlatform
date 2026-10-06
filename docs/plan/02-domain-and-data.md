# 02 - Domain model and data

Conventions: every table has `Id uniqueidentifier` (v7, generated in the domain) unless a different key is
stated. `T` marks a tenant-owned table (`TenantId uniqueidentifier NOT NULL`, indexed first in every
non-unique index). Strings are `nvarchar` with the stated length. All timestamps UTC.

## 1. Identity (`identity`)

**User** (aggregate) - `identity.Users`

| Column | Type | Notes |
|---|---|---|
| TenantId | uniqueidentifier NULL | NULL for platform users |
| Email | 256 | unique, lower-case |
| FullName | 200 | |
| PasswordHash | 500 | `PasswordHasher<User>` (PBKDF2) |
| Role | 32 | see 03 |
| Status | 16 | `Invited`, `Active`, `Inactive` |
| LocationScope | nvarchar(max) | JSON array of location ids; empty = all locations of the tenant |
| PreferredLanguage | 5 | `en` or `ar` |
| FailedLoginCount, LockoutEndsAt, LastLoginAt, CreatedAt, RowVersion | | |

Rules: e-mail unique across the platform; a tenant user cannot have a platform role; the last active
Administrator of a tenant cannot be deactivated or demoted (`USER_LAST_ADMIN`); 5 failed sign-ins lock the
account for 15 minutes.

**RefreshToken** - `identity.RefreshTokens`: `UserId, FamilyId, TokenHash (HMAC-SHA256), ExpiresAt,
RevokedAt, ReplacedById, CreatedIp, CreatedAt`. Index on `TokenHash` (unique).

## 2. Tenancy (`tenancy`)

**Tenant** (aggregate) - `tenancy.Tenants` (not `ITenantOwned`; it is the tenant)

| Column | Type | Notes |
|---|---|---|
| Name | 200 | "Acme Corporation" |
| Code | 32 | unique slug, upper-case |
| Status | 16 | `Active`, `Suspended`, `Archived` |
| LicensingCustomerId | uniqueidentifier NULL | unique when not null; the Customer id in the Licensing Platform |
| Country, City | 100 | |
| TimeZone | 64 | IANA id, default `Africa/Cairo` |
| CustomerSince | date | |
| LogoMediaId | uniqueidentifier NULL | |
| SuspendedAt, SuspensionReason, ArchivedAt, CreatedAt, RowVersion | | |

Behaviour: `Suspend(reason)` (users cannot sign in, devices stay connected but the portal is closed),
`Resume()`, `Archive()` (terminal for the UI: hidden from default lists, devices disconnected with reason
`TENANT_ARCHIVED`). Raises `TenantSuspendedV1`, `TenantResumedV1`, `TenantArchivedV1`.

**Location** (aggregate) `T` - `tenancy.Locations`: `Name (200), Code (32, unique per tenant), City,
Country, AddressLine (300), TimeZone (64), ContactName, ContactEmail, ContactPhone, ImageMediaId NULL,
IsDefault bit, Status (Active|Closed), CreatedAt, RowVersion`. Each tenant has exactly one default location
("Unassigned") that cannot be deleted; devices enrolled without a location code land there.

**LocationEnrollmentCode** `T` - `tenancy.LocationEnrollmentCodes`: `LocationId, CodeHash, CodePrefix (8),
ExpiresAt, MaxUses int NULL, Uses int, RevokedAt, CreatedBy, CreatedAt`. The plain code
(`LOC-XXXXXX-XXXXXX`, 60 bits) is returned once.

## 3. Licensing (`licensing`) - cache of the Licensing Platform, never the source of truth

**TenantEntitlement** `T` - `licensing.TenantEntitlements` (PK `TenantId`)

| Column | Notes |
|---|---|
| PlanCode (32), PlanName (200) | from the customer's current subscription for the product |
| SubscriptionStatus (16) | `Trial`, `Active`, `Suspended`, `Cancelled`, `Expired`, `None` |
| Features nvarchar(max) | JSON array of feature codes |
| MaxDevices int NULL | sum of `maxActivations` over usable licences; NULL = unlimited |
| ActiveSeats int | sum of `activeActivations` |
| StartsAt, RenewsAt datetimeoffset NULL | `RenewsAt` = subscription end date |
| LicenseIds nvarchar(max) | JSON array |
| SyncedAt, SyncError (500) NULL | |

**DeviceLicense** `T` - `licensing.DeviceLicenses` (PK `DeviceId`): `LicenseId, LicenseNumber (64),
State (Licensed|Unlicensed), ReasonCode (64) NULL, UnlicensedSince NULL, Token nvarchar(max) NULL, Kid (64) NULL,
CheckAfter, OfflineValidUntil, LastCheckedAt`.

**LicensingSyncState** - `licensing.SyncState` (single row): `Cursor (200) NULL, LastSuccessAt,
LastAttemptAt, LastError (1000) NULL, ConsecutiveFailures int`.

**EnrollmentAttempt** `T NULL` - `licensing.EnrollmentAttempts`: `TenantId NULL, KeyPrefix (6), DeviceFingerprint
(128), Hostname (200), Ip (64), Succeeded bit, ErrorCode (64) NULL, At`. Kept 90 days.

## 4. Devices (`devices`)

**Device** (aggregate) `T` - `devices.Devices`

| Column | Type | Notes |
|---|---|---|
| LocationId | uniqueidentifier | |
| Name | 200 | display name, defaults to the host name, editable |
| Hostname | 200 | |
| Fingerprint | 128 | the agent's stable device id (also the Licensing `deviceId`); unique per tenant |
| OsFamily | 16 | `Windows`, `Linux`, `MacOS`, `Other` |
| OsName (200), OsVersion (100), Architecture (32) | | |
| AgentVersion (32), ProtocolVersion int | | |
| LocalIp (64), PublicIp (64) NULL, MacAddress (32) NULL | | |
| Status | 16 | `Active`, `Retired` |
| EnrolledAt, RetiredAt NULL, RowVersion | | |

Indexes: unique `(TenantId, Fingerprint)`; `(TenantId, LocationId, Name)`.
Behaviour: `Rename`, `MoveTo(locationId)`, `UpdateAgentInfo(...)`, `Retire()` (releases the seat through an
integration event, revokes the credential, keeps history).

**DeviceCredential** `T` - `devices.DeviceCredentials` (PK `DeviceId`): `SecretHash (HMAC-SHA256 with the
server pepper), IssuedAt, RotatedAt NULL, RevokedAt NULL`.

**DeviceState** (read model, not an aggregate) `T` - `devices.DeviceStates` (PK `DeviceId`)

| Column | Type |
|---|---|
| LocationId | uniqueidentifier |
| Connection | tinyint (0 Offline, 1 Online) |
| Health | tinyint (0 Unknown, 1 Healthy, 2 Warning, 3 Critical) |
| LicenseState | tinyint (0 Unlicensed, 1 Licensed) |
| OsFamily | tinyint |
| CpuPercent, RamPercent, DiskPercent | decimal(5,2) NULL |
| UptimeSeconds | bigint NULL |
| OpenCritical, OpenWarning | int |
| LastSeenAt, LastTelemetryAt, ConnectedSince | datetime2(0) NULL |
| AppliedConfigVersion | int NULL |
| LastEventSequence | bigint |
| UpdatedAt | datetime2(0) |

Indexes: `IX_DeviceStates_Tenant_Location (TenantId, LocationId) INCLUDE (Connection, Health, LicenseState,
OsFamily, CpuPercent, RamPercent, DiskPercent)`; `IX_DeviceStates_Tenant_Health (TenantId, Health, Connection)`.
`DiskPercent` is the highest used-percentage among the device's fixed partitions.

**DeviceInventoryDocument** `T` - `devices.InventoryDocuments` (PK `DeviceId, Kind`): `Kind (16:
Hardware|Os|Network|Disks|Programs|Services|Users|Sensors), Json varbinary(max) (Brotli-compressed UTF-8),
Hash (64), UpdatedAt`. Written only when the hash changes.

## 5. Telemetry (`telemetry`) - no EF change tracking, written with `SqlBulkCopy`

**MetricMinute** - `telemetry.MetricMinutes`, clustered PK `(DeviceId, BucketUtc)`, page compression

| Column | Type |
|---|---|
| TenantId | uniqueidentifier |
| BucketUtc | datetime2(0) (start of the minute) |
| CpuAvg, CpuMax, CpuP95 | decimal(5,2) |
| RamAvg, RamMax | decimal(5,2) |
| DiskActiveAvg | decimal(5,2) NULL |
| DiskReadBps, DiskWriteBps | bigint NULL |
| DiskResponseMs | decimal(9,2) NULL |
| NetRxBps, NetTxBps | bigint NULL |
| NetRxBytes, NetTxBytes | bigint NULL (bytes in this minute) |
| PingMs | decimal(9,2) NULL |
| PacketLossPercent | decimal(5,2) NULL |
| TempMaxC | decimal(5,1) NULL |
| Samples | smallint |

Extra index: `(TenantId, BucketUtc)`.

**MetricHour** - `telemetry.MetricHours`: same columns aggregated (avg of avgs weighted by `Samples`, max of
maxes, P95 = max of minute P95s), PK `(DeviceId, BucketUtc)`.

**DiskUsageHour** - `telemetry.DiskUsageHours`, PK `(DeviceId, Drive, BucketUtc)`: `TenantId, Drive (16),
Label (100), FileSystem (16), TotalGb, UsedGb, FreeGb decimal(12,2)`.

**MonitorPointSample** - `telemetry.MonitorPointSamples`, PK `(MonitorPointId, BucketUtc)`: `TenantId,
DeviceId, Status tinyint, ResponseMs decimal(9,2) NULL` (one row per minute per point).

**LiveSnapshot** - `telemetry.LiveSnapshots` (PK `DeviceId`): `TenantId, Json varbinary(max) (compressed
latest full snapshot: CPU detail, RAM detail, disk activity, network detail, top-5 lists, partitions),
CapturedAt`. Overwritten at most once per minute per device.

Retention (job `TelemetryRetention`, daily, deletes in batches of 50,000): minutes 30 days, hours 400 days,
disk usage 400 days, monitor-point samples 90 days. Values come from `Telemetry:Retention:*`.

## 6. Monitoring (`monitoring`)

**MonitorPoint** (aggregate) `T` - `monitoring.MonitorPoints`: `DeviceId, Key (64; the agent's id, unique per
device), DisplayName (200), Type (16: Website|Database|Ping|Application|Service|Disk|Network|Custom),
Target (500), IntervalSeconds int, AlertLevel (16: Problem|Warning|Unknown), Enabled bit, ShowInShortcut bit,
IconMediaId NULL, SettingsJson nvarchar(max) NULL (no secrets), Origin (8: Agent|Cloud), SortOrder int,
RowVersion`.

**MonitorPointState** `T` - `monitoring.MonitorPointStates` (PK `MonitorPointId`): `DeviceId, Status tinyint,
Message (500), ResponseMs, LastCheckedAt, StatusSince`.

**Alert** (aggregate) `T` - `monitoring.Alerts`

| Column | Notes |
|---|---|
| LocationId, DeviceId | |
| IssueKey (128) | stable per condition: `cpu`, `ram`, `disk-C`, `point:{key}`, `sensor:{name}`, `hw:{name}`, `device-offline`, `license` |
| Category (16) | `Performance`, `Storage`, `Service`, `Application`, `Database`, `Connectivity`, `System`, `License` |
| Severity (8) | `Critical`, `Warning`, `Info` |
| Title (200), Message (2000) | |
| Status (12) | `Open`, `Resolved` |
| Source (8) | `Agent`, `Cloud` |
| FirstSeenAt, LastSeenAt, Occurrences int | |
| AcknowledgedAt, AcknowledgedByUserId | NULL |
| ResolvedAt, ResolvedBy (8: Auto\|User) | NULL |

Unique filtered index `(DeviceId, IssueKey) WHERE Status = 'Open'`. Index `(TenantId, Status, Severity,
LastSeenAt DESC)`; `(TenantId, LocationId, FirstSeenAt DESC)`.
Behaviour: `Raise` (creates or, if open, `Touch`: bumps `LastSeenAt`, `Occurrences`, may change severity),
`Acknowledge(user)`, `Resolve(by)`. Raises `AlertRaisedV1`, `AlertSeverityChangedV1`, `AlertResolvedV1`.

**AlertDailyStat** `T` - `monitoring.AlertDailyStats` (PK `TenantId, LocationId, Day`): `Critical, Warning,
Info int` (alerts opened that day, tenant-local date). Maintained by the `AlertRaisedV1` handler.

Device health rule (domain service `DeviceHealthCalculator`): `Offline -> Unknown`; else `Critical` if any
open critical alert, `Warning` if any open warning alert, otherwise `Healthy`.

## 7. Notifications (`notifications`)

- **AlertChannelSettings** `T` (PK `TenantId`): `EmailEnabled, SmsEnabled, InAppEnabled, WebhookEnabled bit,
  WebhookUrl (500) NULL, WebhookSecretProtected NULL, UpdatedAt`. Defaults: e-mail and in-app on.
- **AlertRecipient** `T`: `Name (200), Email (256), Events (24: All|CriticalOnly|WarningsAndCritical),
  LocationId NULL, IsActive`.
- **Notification** `T NULL` (NULL = platform feed): `UserId NULL (NULL = every user of the scope), Severity,
  Category, Title (200), Body (1000), LocationId NULL, DeviceId NULL, AlertId NULL, CreatedAt`.
  Index `(TenantId, CreatedAt DESC)`.
- **NotificationRead** : `NotificationId, UserId, ReadAt` (PK both).
- **NotificationDelivery** `T`: `AlertId, Channel (8: Email|Webhook), Recipient (256), Status (8:
  Pending|Sent|Failed), Attempts, LastError (500), SentAt`.

## 8. Configuration (`config`)

- **DeviceConfiguration** `T` (PK `DeviceId`): `Version int, DocumentJson nvarchar(max), UpdatedByUserId,
  UpdatedAt, RowVersion`. The document schema is in 05 section 8.
- **DeviceConfigurationAck** `T` (PK `DeviceId`): `AppliedVersion int, AppliedAt, Error (500) NULL`.

## 9. Reports (`reports`)

**GeneratedReport** `T`: `Type (32), Title (200), ParametersJson, Format (4: Pdf|Csv), Status (10:
Queued|Running|Done|Failed), MediaId NULL, SizeBytes, RequestedByUserId, RequestedAt, CompletedAt, Error`.

Report types: `overview`, `location-summary`, `device-health`, `performance`, `network-usage`,
`alerts`, `license-usage`, `custom` (as in the Reports design).

## 10. Archive (`archive`) - customer records

- **CustomerProfile** `T` (PK `TenantId`): `Industry (100), Website (200), Phone (50), Address (300),
  AccountManager (200), UpdatedAt`.
- **ArchiveContact** `T`: `Name, Email, Phone, JobTitle, IsPrimary`.
- **ArchiveNote** `T`: `Body (4000), IsInternal bit, AuthorUserId, CreatedAt`.
- **ArchiveFile** `T`: `MediaId, DisplayName (200), SizeBytes, UploadedByUserId, UploadedAt, IsInternal bit`.
- **RemoteAccessEntry** `T`: `LocationId NULL, DeviceId NULL, Tool (32), Label (200), IdentifierProtected,
  PasswordProtected NULL, CreatedAt`. Platform roles only; values encrypted with ASP.NET Data Protection;
  every read is audited.

`IsInternal` rows are never returned to customer roles (enforced in the query handler and tested).

## 11. Audit, Messaging, Media

- `audit.AuditRecords`: `TenantId NULL, ActorType (8: User|Device|System), ActorId, ActorName, Action (100),
  EntityType (64), EntityId (64), Success bit, Details (2000), Ip (64), CorrelationId (64), At`.
  Index `(TenantId, At DESC)`. Append-only: no update or delete path exists in the code.
- `messaging.OutboxMessages`: `Id, Type (200), Payload nvarchar(max), TenantId NULL, OccurredAt, ProcessedAt,
  Attempts, NextAttemptAt, LastError, DeadLettered`. `messaging.InboxMessages`: `MessageId, Handler, ProcessedAt`.
- `media.MediaFiles`: `TenantId NULL, FileName, ContentType, SizeBytes, Sha256, StoragePath, CreatedAt`.
  Files are stored under `App_Data/media/{tenant}/{id}`; allowed types and a 10 MB limit are validated.

## 12. Domain events to integration events

| Raised by | Event | Handlers |
|---|---|---|
| Device enrolled | `DeviceEnrolledV1` | create `DeviceState`, default `DeviceConfiguration`, audit, notification |
| Presence monitor | `DeviceWentOfflineV1` / `DeviceCameOnlineV1` | raise/resolve `device-offline` alert, recompute health, push SignalR |
| Alert | `AlertRaisedV1`, `AlertSeverityChangedV1`, `AlertResolvedV1` | update `DeviceState` counts + health, daily stats, notifications, SignalR |
| Licensing sync | `EntitlementChangedV1` | invalidate caches, re-evaluate device licence states, notification when expiring |
| Tenant | `TenantSuspendedV1`, `TenantArchivedV1` | revoke refresh tokens, disconnect devices when archived |
| Device retired | `DeviceRetiredV1` | release seat in Licensing, close session, resolve open alerts |
