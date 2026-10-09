# Operations

How to configure, release, back up and run Monitor Cloud (MC-1005). One API process serves REST (port 5300 behind
the proxy), the gRPC agent gateway (5301) and SignalR. The portal is static files behind the same proxy.

## 1. Configuration reference

Values come from `appsettings.json`, then environment variables (`__` as the separator, e.g. `Jwt__SigningKey`,
`Cors__Origins__0`). Secrets are never in a file outside Development.

**Production check.** In `Production` the API refuses to start and lists every problem (names only, never values)
when: the connection string is empty; a JWT key is shorter than 32 characters, contains `DEV-ONLY`/`TEST-ONLY`/
`CI-ONLY`, or both keys are equal; `Seed:DemoData` is true; `Licensing:Mode` is not `Live`; `Cors:Origins` is empty
or holds a non-https or local origin; `Agent:PublicBaseUrl`, `Agent:GatewayUrl` or `Portal:BaseUrl` is not a public
https address; `Storage:Root` is empty; `AllowedHosts` is `*`; or the log level is Debug/Verbose.

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings:Monitor` | - | SQL Server. Required. |
| `Jwt:SigningKey` | - | HS256 key of user tokens, >= 32 characters. Also keys the hashes of refresh and invitation secrets. |
| `Jwt:DeviceSigningKey` | - | HS256 key of device tokens, >= 32 characters, different from the user key. Also keys the hashes of device secrets. |
| `Jwt:PreviousSigningKeys` / `Jwt:PreviousDeviceSigningKeys` | `[]` | Keys replaced by a rotation (section 5). |
| `Jwt:Issuer` / `Audience` / `DeviceAudience` | `monitor-cloud` / `monitor-cloud-portal` / `monitor-agent-gateway` | |
| `Jwt:AccessTokenMinutes` / `RefreshTokenDays` / `DeviceTokenMinutes` | 15 / 14 / 60 | |
| `Cors:Origins` | `[]` | The portal origin(s), https. |
| `AllowedHosts` | `*` | Public host names of the API, `;`-separated. |
| `Portal:BaseUrl` | `http://localhost:4300` | Used in invitation links. |
| `Seed:ApplyMigrations` | `false` | `true` only in Development; production applies migrations as a release step (section 3). |
| `Seed:DemoData` | `false` | Demo seed; refuses to run in Production. |
| `Seed:AdminEmail` / `Seed:AdminPassword` | - | First platform administrator, created once when no platform user exists. Remove after the first start. |
| `Licensing:Mode` | `Fake` | `Live` in production. |
| `Licensing:BaseUrl` / `ClientId` / `ClientSecret` / `ProductCode` | - / - / - / `000001` | Licensing Platform client (secret from the environment). |
| `Licensing:SyncIntervalSeconds` / `FullReconcileHours` | 60 / 24 | Change feed polling and full reconcile. |
| `Licensing:EntitlementStaleAfterMinutes` / `FailOpenHours` / `UnlicensedGraceDays` / `ExpiringSoonDays` | 30 / 72 / 14 / 30 | See section 6.1. |
| `Agent:PublicBaseUrl` / `Agent:GatewayUrl` | `http://localhost:5300` | Addresses handed to agents at enrollment. |
| `Agent:EnrollPerFingerprintPerHour` / `BackgroundJobsEnabled` | 5 / `true` | |
| `Gateway:HeartbeatSeconds` / `MissedHeartbeats` / `OfflineGraceSeconds` | 30 / 3 / 15 | Offline detection. |
| `Gateway:HelloTimeoutSeconds` / `SessionLifetimeHours` / `MaxBatchMinutes` | 10 / 12 / 60 | |
| `Gateway:CloneReplacements` / `CloneWindowMinutes` | 3 / 5 | Possible cloned device notification. |
| `Telemetry:ChannelCapacity` / `FlushRows` / `FlushSeconds` | 2000 / 1000 / 2 | Ingestion buffer (section 6.2). |
| `Telemetry:RetentionMinuteDays` / `RetentionHourDays` / `RetentionDiskDays` | 30 / 400 / 400 | Used until Platform Settings are saved; then those values apply. |
| `Storage:Root` | `{content root}/App_Data` | Persistent folder: `media/` (report and archive files), `keys/` (Data Protection keys), `pdf/` (temporary). |
| `Storage:ChromiumPath` | auto | Browser for PDF reports; empty = Edge/Chrome/Chromium in the usual places. Without one, PDF is reported as unavailable. |
| `RateLimiting:Auth:PermitLimit` / `Enroll` / `AgentToken` | 20 / 10 / 120 per minute and IP | 429 `RATE_LIMITED` with `Retry-After`. |
| `ReverseProxy:KnownProxies` / `ReverseProxy:KnownNetworks` | `[]` | Addresses (e.g. `10.0.0.5`) or networks (e.g. `10.0.0.0/8`) of the reverse proxy. `X-Forwarded-For` / `X-Forwarded-Proto` are trusted only from them. **Set this behind a proxy**, or every client shares the proxy's rate limit and HTTPS is not detected. |
| `Outbox:PollInterval` / `BatchSize` / `Enabled` | `00:00:02` / 50 / `true` | |
| `Serilog:*` | console, Information | Structured logs; secrets are never logged (tested). |

Platform Settings (`/admin/settings`, stored in the database) hold the branding name, the default offline-alert
delay, the retention days and the e-mail sender.

## 2. Health

- `GET /health/live` - the process is up (no dependency checks). Use for restarts.
- `GET /health/ready` - database reachable, outbox lag under 5 minutes (degraded above), gateway running. Use for
  load-balancer traffic.
- `GET /api/v1/platform/licensing/status` (platform admin) - Licensing mode, last sync, failures.

## 3. Migrations as a release step

Migrations are idempotent scripts applied before the new version starts:

```bash
dotnet ef migrations script --idempotent -p src/MonitorCloud.Infrastructure -s src/MonitorCloud.Api -o migrate.sql
```

1. Back up the database (section 4).
2. Run `migrate.sql` with a login that may change the schema.
3. Deploy the new API (it only needs data permissions), then the portal files.
4. Check `/health/ready` and sign in.

Every migration is additive up to the release that uses it, so the previous API version keeps working while the
script runs. CI fails when the model has changes without a migration.

## 4. Backup and restore

What to back up, together, at the same moment:

| Item | How | Why |
|---|---|---|
| SQL Server database | Full backup daily, log backups every 15 minutes (full recovery model) | All business data |
| `{Storage:Root}/media` | File backup after the database backup | Report outputs and archive attachments (rows point to these files) |
| `{Storage:Root}/keys` | File backup; keep it as secret as the JWT keys | Without the Data Protection keys, remote-access entries and webhook secrets cannot be decrypted |
| JWT keys, Licensing client secret | The secret store of the host | Not in the database |

Restore:

1. Stop the API.
2. Restore the database (`RESTORE DATABASE ... WITH NORECOVERY`, then the logs, then `WITH RECOVERY`).
3. Restore `media/` and `keys/` from the same point in time.
4. Start the API with the same keys. Check `/health/ready`, open a report download and reveal one remote-access
   entry as a test.
5. Agents reconnect on their own. Telemetry sent while the API was down is resent from the agents' local outbox
   (200 MB per agent).

Test a restore on a copy every quarter.

## 5. Key rotation (JWT, device, command-signing)

**User key (`Jwt:SigningKey`).**

1. Set the new key as `Jwt__SigningKey` and the old one as `Jwt__PreviousSigningKeys__0`. Restart.
2. Old access tokens stay valid until they expire (15 minutes). Old refresh tokens still work, and each use issues
   a new token hashed with the new key. Pending invitations still work.
3. After 14 days (refresh token lifetime) and the 7-day invitation lifetime, remove the previous key and restart.
   Users that did not sign in during that time sign in again.

**Device key (`Jwt:DeviceSigningKey`).**

1. Set the new key and move the old one to `Jwt__PreviousDeviceSigningKeys__0`. Restart.
2. Each agent exchanges its secret for a token at least every 60 minutes. On that exchange the stored secret hash
   is replaced by one under the new key.
3. Remove the previous key only after every active device connected once (devices offline for longer keep their old
   hash and would have to re-enroll). Check the Devices list for devices offline longer than the overlap; 30 days is
   a safe overlap.

**After a leak** of a key, rotate without the previous key. Every user signs in again. Every agent re-enrolls with
its product key (secrets cannot be verified any more).

**Command-signing keys** belong to the Licensing Platform. Monitor Cloud only passes the current public key set to
agents at enrollment and on reconnect. Rotate them in the Licensing Platform (its runbook); agents receive the new
set on the next connection.

**Data Protection keys** (`{Storage:Root}/keys`) roll over by themselves every 90 days and old keys stay for
decryption. Never delete that folder.

## 6. Runbooks

### 6.1 Licensing Platform unavailable

Symptoms: `/platform/licensing/status` shows failures; `/health/ready` reports "Licensing sync is failing"
(degraded); enrollments answer 503 `LICENSING_UNAVAILABLE`; the log has "Licensing sync failed with ...".

- Existing devices keep working. Entitlements are cached; after `EntitlementStaleAfterMinutes` the portal shows them
  as stale, and they stay valid for `FailOpenHours` (72 h) before features close.
- Enrollment and seat release need the Licensing Platform, so new devices wait. Agents retry with backoff.
- Check the network path and the client credentials (`Licensing:BaseUrl`, `ClientId`, secret). A 401 from Licensing
  means the secret changed.
- When it is back: Plans > "Sync now" (or `POST /platform/licensing/sync`). Confirm the status is healthy and the
  failure count is 0.
- Over 48 h down: tell the customers that new devices cannot be added. Plan the 72 h fail-open limit with the
  Licensing team.

### 6.2 Gateway overloaded

Symptoms: ingestion acknowledgements slow (agents log retries and their outbox grows), high CPU, slow API answers.
The telemetry buffer applies back-pressure when it is full: the gateway waits instead of dropping data, so slow
acknowledgements are the visible sign.

- Look at the number of connected sessions (platform dashboard) and the log for reconnect storms (many `Hello` per
  second). After an outage all agents reconnect at once. They back off with jitter, so the load falls within minutes.
- If the database is the bottleneck (slow `FlushRows` batches), check section 6.3 and the SQL Server wait stats.
  Raise `Telemetry:FlushRows` (2000) and `FlushSeconds` (5) to write fewer, larger batches.
- If ingestion is the bottleneck, raise the default sample interval for customers (Settings > Monitoring,
  `sampleSeconds` 10). Agents receive it with the next configuration push.
- Agents never lose data during overload: unacknowledged batches stay in their local outbox and are resent.
- Long term: move the gateway to its own instance (the AgentGateway project is a separate assembly) and scale the
  database.

### 6.3 Database full

Symptoms: errors "could not allocate space", `/health/ready` unhealthy, ingestion stops.

1. Find the largest tables:

   ```sql
   SELECT s.name + '.' + t.name AS [table], SUM(a.total_pages) * 8 / 1024 AS mb
   FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
   JOIN sys.indexes i ON i.object_id = t.object_id JOIN sys.partitions p ON p.object_id = i.object_id AND p.index_id = i.index_id
   JOIN sys.allocation_units a ON a.container_id = p.partition_id
   GROUP BY s.name, t.name ORDER BY mb DESC;
   ```

2. Usually `telemetry.MetricMinutes` is the largest. Lower the minute retention in Platform Settings (minimum 7 days)
   and let the daily clean-up run, or start it at once by restarting the API (the clean-up runs on start).
3. If the transaction log is full, take a log backup (full recovery) and check that log backups run.
4. Grow the data file or the disk. Do not shrink the database: it fragments the indexes.
5. Check `audit.AuditRecords` and `notifications.NotificationDeliveries` growth monthly. They are kept for the
   audit period agreed with customers; archive them before deleting.
