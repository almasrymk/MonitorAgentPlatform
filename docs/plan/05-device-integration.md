# 05 - Integration 2: customer devices

The agent on each device opens **one outbound** connection to Monitor Cloud. Nothing listens on the
customer's network. The wire contract is `proto/monitor/agent/v1/agent.proto`.

## 1. Endpoints used by the agent

| Step | Transport | Endpoint | Auth |
|---|---|---|---|
| Enroll | HTTPS JSON | `POST /api/agent/v1/enroll` | product key in the body |
| Get device token | HTTPS JSON | `POST /api/agent/v1/token` | device id + device secret |
| Rotate secret | HTTPS JSON | `POST /api/agent/v1/credential/rotate` | device token |
| Stream | gRPC over HTTP/2 + TLS | `monitor.agent.v1.AgentGateway/Connect` | `Authorization: Bearer <device token>` |

Enrollment and token exchange are plain HTTPS so that a blocked gRPC path still produces a clear error on
the agent ("enrolled, but the stream cannot connect").

### 1.1 Enroll

Request:

```json
{ "productKey": "XXXXXX-XXXXXX-XXXXXX-XXXXXX-XXXXXX", "fingerprint": "ma-9f2c...", "hostname": "WEB-SRV-01",
  "osFamily": "Windows", "osName": "Windows Server 2019", "osVersion": "10.0.17763", "architecture": "x64",
  "agentVersion": "1.1.0", "protocolVersion": 1, "locationCode": "LOC-ABC234-XYZ789" }
```

`fingerprint` must satisfy the Licensing rule (8-128 characters of letters, digits, `- _ : .`).
`locationCode` is optional.

Response `200`:

```json
{ "deviceId": "0199...", "tenantName": "Acme Corporation", "locationName": "Cairo HQ",
  "deviceSecret": "base64url-256-bit", "tokenUrl": "/api/agent/v1/token",
  "gatewayUrl": "https://gateway.example.com", "commandSigningKeys": { "keys": [ ... ] },
  "license": { "state": "Licensed", "token": "<signed ES256 token>", "checkAfter": "2026-10-07T08:00:00Z",
               "signingKeys": { "keys": [ ... ] } } }
```

Errors (problem details, `code`): any `LIC_*` code from Licensing unchanged; `ENROLL_INVALID_LOCATION_CODE`;
`ENROLL_TENANT_NOT_ACTIVE`; `ENROLL_PROTOCOL_UNSUPPORTED`; `LICENSING_UNAVAILABLE` (503); `RATE_LIMITED`.
Rate limit: 10 per minute per IP, 5 per hour per fingerprint.

The handler is `EnrollDeviceCommand` in `Application/Devices`. It calls `ILicensingGateway.ActivateSeatAsync`
(04 section 4.1), resolves the location (`locationCode` -> its location, else the tenant's default location),
creates the device, credential, state, licence row and default configuration in one transaction, and raises
`DeviceEnrolledV1`.

### 1.2 Token

`{ "deviceId": "...", "deviceSecret": "..." }` -> `{ "accessToken": "...", "expiresIn": 3600 }`.
The secret is compared in constant time against its HMAC. Failures: `DEVICE_INVALID_CREDENTIAL` (401, same
answer for unknown device, wrong secret, revoked credential, retired device). 5 failures in 10 minutes for a
device id block that id for 10 minutes. The agent renews 5 minutes before expiry.

The token is checked when the stream opens. A stream may outlive its token; the gateway ends every stream
after 12 hours with `Disconnect { code: "SESSION_ROTATE", retry_after_seconds: 1-10 }` so each device
re-authenticates at least twice a day. Revoking a credential or retiring a device closes its stream at once.

## 2. Stream lifecycle

```
Agent                                         Gateway
  |-- Connect (Bearer device token) ----------->|  authenticate, load Device (Active? tenant not archived?)
  |-- Hello ----------------------------------->|  register session, mark Online, update agent info
  |<-- Welcome (heartbeat, last_received_seq, config_version, license_state)
  |<-- ConfigUpdate (if version differs) -------|
  |-- MonitorPointReport(full=true) ----------->|
  |-- InventoryUpdate x N (changed kinds) ----->|
  |-- resend guaranteed messages > last_received_sequence
  |== steady state ==============================
  |-- Heartbeat every 30 s -------------------->|
  |-- MetricBatch every 60 s ------------------>|  -> Ack(sequence) after durable write
  |-- IssueEvent / MonitorPointReport on change>|  -> Ack
  |-- Snapshot every 60 s --------------------->|
  |<-- SetTelemetryMode(LIVE, 2 s, ttl 60 s) ---|  when a user opens the device screen
  |-- LiveSample every 2 s -------------------->|  -> SignalR only
  |-- Goodbye(SERVICE_STOPPING) --------------->|  graceful stop
```

The gRPC service is a thin adapter over `IAgentTransport` (`ReadAsync` / `WriteAsync` of envelopes). Session
logic, routing and presence reference only that interface, so another transport can be added without touching
them.

Rules implemented by the gateway (`MonitorCloud.AgentGateway`):

1. The first message must be `Hello` within 10 s, otherwise the stream is closed.
2. `protocol_version` outside the supported range -> `Disconnect PROTOCOL_UNSUPPORTED`.
3. **One session per device.** A second connection for the same device replaces the first, which receives
   `Disconnect DUPLICATE_SESSION` (retry 0). Three replacements within 5 minutes raise a platform alert
   "possible cloned device" for that device.
4. **Guaranteed messages** carry `sequence > 0`. The gateway persists `DeviceStates.LastEventSequence`; a
   message with `sequence <= LastEventSequence` is acknowledged and ignored (duplicate). Processing is
   idempotent anyway (`MetricMinutes` primary key, alert `IssueKey`).
5. `Ack` is sent after the batch that contains the message is committed, with the highest contiguous
   sequence.
6. Backlog after an outage: the agent sends `MetricBatch`es of at most `max_batch_minutes` minutes, oldest
   first. Minutes older than the retention window are dropped by the gateway but still acknowledged.
7. Alerts for backlog `IssueEvent`s are raised with their original `occurred_at` and do **not** create
   notifications when older than 15 minutes.
8. Clock skew: when `|sent_at - server time| > 5 min` the device gets a `System` warning alert
   `clock-skew`; bucket times are still trusted.
9. An `Unlicensed` device is processed normally during its grace period (00 D19). After it, only `Hello`,
   `Heartbeat`, `Goodbye` and `ConfigApplied` are processed; all other bodies are acknowledged and dropped.
10. Back-pressure: when the ingestion channel is full the gateway stops reading from streams (gRPC flow
    control); it never buffers unbounded.
11. Server shutdown: `Disconnect SERVER_SHUTDOWN` with `retry_after_seconds` = random 5-30.

### Presence

- `IAgentSessionRegistry` (in-memory, `ConcurrentDictionary<Guid, AgentSession>`; interface ready for a
  distributed implementation) maps device id to the live session.
- `PresenceMonitor` (every 10 s): a session with no message for `3 x heartbeat_seconds` is closed and the
  device marked `Offline`. A closed stream marks the device `Offline` after a 15 s grace period (covers quick
  reconnects). `Goodbye` marks it `Offline` immediately with the reason stored.
- `DeviceWentOfflineV1` raises the cloud alert `device-offline` (severity from tenant setting, default
  `Critical`) **2 minutes** after the device went offline if it is still offline; `HOST_SHUTTING_DOWN` and
  `UPDATING` goodbyes use `Info`. `DeviceCameOnlineV1` resolves it.
- When every online device of a location goes offline within 60 s, one location-level notification is sent
  instead of one per device (alerts are still created per device).

## 3. Ingestion pipeline

```
gRPC service -> AgentMessageRouter -> per-type handler
   MetricBatch        -> Channel<MetricRow>      -> TelemetryWriter (SqlBulkCopy, DeviceStates update)
   LiveSample         -> ILiveBroadcaster        -> SignalR group device:{id} (throttled to the requested interval)
   Snapshot           -> ILiveSnapshotStore      -> memory + telemetry.LiveSnapshots (1/min)
   InventoryUpdate    -> ISender.Send(UpsertInventoryCommand)
   IssueEvent         -> ISender.Send(ApplyIssueEventCommand)      (Monitoring)
   MonitorPointReport -> ISender.Send(ApplyMonitorPointReportCommand)
   ConfigApplied      -> ISender.Send(RecordConfigAppliedCommand)
   CommandResult      -> ISender.Send(RecordCommandResultCommand)
```

`TelemetryWriter`: batches of up to 1,000 rows or 2 s; writes `MetricMinutes` and `DiskUsageHours`
idempotently; updates `DeviceStates` (`CpuPercent = cpu_avg` of the newest minute, `RamPercent`,
`DiskPercent = disk_percent_max`, `UptimeSeconds`, `LastTelemetryAt`, `LastEventSequence`) in one set-based
statement; then completes the acknowledgements. On a database failure the batch is retried with back-off and
streams stay un-acknowledged (the agent keeps the data).

`MetricRollupJob` (every 5 minutes): aggregates completed hours into `MetricHours` (idempotent `MERGE` by
key). `MonitorPointSamples`: one row per point per minute written from the in-memory state by
`MonitorPointSampler`.

## 4. Live mode

1. The device screen calls `POST /api/v1/devices/{id}/live-sessions` when it opens and every 30 s while
   visible; the handler records interest (`ILiveInterestRegistry`, in-memory, TTL 60 s).
2. On the first interest the gateway sends `SetTelemetryMode { LIVE, 2, 60 }`; it re-sends it every 30 s while
   any interest remains. With no renewal the agent falls back to `NORMAL` by itself.
3. `LiveSample`s are forwarded to SignalR group `device:{id}` and not stored.
4. The device list never triggers live mode: cards show `DeviceStates` values with "last seen".

## 5. Snapshot document (JSON inside `Snapshot.json_brotli`)

```json
{ "capturedAt": "2026-10-06T10:11:33Z",
  "cpu": { "usage": 27, "physicalCores": 8, "logicalCores": 16, "speedGhz": 2.4, "maxSpeedGhz": 2.8, "tempC": 52, "processes": 248, "model": "..." },
  "ram": { "usage": 36, "totalGb": 31.7, "usedGb": 11.4, "freeGb": 20.3, "cachedGb": 2.1 },
  "diskActivity": { "activePercent": 5, "readBps": 0, "writeBps": 110592, "responseMs": 5.5 },
  "partitions": [ { "drive": "C:", "label": "", "fileSystem": "NTFS", "totalGb": 169, "usedGb": 156, "freeGb": 13, "usage": 92 } ],
  "network": { "adapter": "Intel(R) Ethernet", "type": "Ethernet", "downloadBps": 0, "uploadBps": 0, "publicIp": "203.0.113.10", "localIp": "192.168.1.50", "pingMs": 22, "lossPercent": 0, "lastSpeedTest": { "downloadMbps": 18.2, "uploadMbps": 9.1, "at": "..." } },
  "top": { "cpu": [ { "name": "w3wp.exe", "pid": 13408, "value": 12 } ], "ram": [ ... ], "disk": [ ... ], "network": [ ... ] },
  "service": { "status": "Running", "uptimeSeconds": 1052880 } }
```

Each `top` list has 5 entries. Units: CPU %, RAM MB, disk and network KB/s.

## 6. Inventory documents

`Hardware` (CPU, memory modules, motherboard, BIOS, GPU), `Os`, `Network` (adapters, DNS, gateway),
`Disks` (physical disks with model, type, size, health), `Programs` (name, publisher, version, install date),
`Services` (name, display name, state, start mode, account, health), `Users` (accounts and sessions),
`Sensors`. The agent already builds all of these for its local API; the connector serialises the same DTOs.
A document is sent after `Hello` only when its hash differs from the last acknowledged hash, and afterwards
on change (programs/services/users are checked every 5 minutes, hardware every 6 hours).

## 7. Mapping agent issues to alerts

The agent already raises issues with stable ids. The connector converts them:

| Agent issue id | `issue_key` | Category | Severity |
|---|---|---|---|
| `cpu` | `cpu` | Performance | as evaluated (Warning/Critical) |
| `ram` | `ram` | Performance | as evaluated |
| `disk-{drive}` | `disk-{drive}` | Storage | as evaluated |
| `point:{id}` (website, API) | `point:{key}` | Connectivity | Critical when down, Warning when degraded |
| `database:{id}` | `point:{key}` | Database | Critical |
| application monitor points | `point:{key}` | Application | Critical |
| service state problems | `service:{name}` | Service | Warning/Critical |
| `sensor:{name}` | `sensor:{name}` | System | Critical |
| `hw:{name}` (specification mismatch) | `hw:{name}` | System | Warning |
| internet down | `internet` | Connectivity | Critical |

Only **changes** are sent: `RAISED` when an issue appears, `SEVERITY_CHANGED`, `CLEARED` when it disappears.
A condition must hold for the configured duration before `RAISED` (section 8).

## 8. Device configuration document (`ConfigUpdate.json_brotli`)

```json
{ "version": 12,
  "telemetry": { "sampleSeconds": 5 },
  "thresholds": {
    "cpu":  { "warningPercent": 80, "criticalPercent": 95, "forSeconds": 300, "clearBelowPercent": 75 },
    "ram":  { "warningPercent": 80, "criticalPercent": 95, "forSeconds": 300, "clearBelowPercent": 75 },
    "disk": { "warningPercent": 85, "criticalPercent": 92, "forSeconds": 60 },
    "tempC": { "critical": 85, "forSeconds": 120 } },
  "monitorPoints": [
    { "key": "db-main", "displayName": "Database", "type": "Database", "target": "localhost/ORGDb", "intervalSeconds": 30,
      "alertLevel": "Problem", "enabled": true, "showInShortcut": true, "settings": { "engine": "SqlServer", "port": 1433 }, "secretRef": "db-main" } ],
  "features": { "remoteActions": false } }
```

- Secrets never travel in this document. `secretRef` names a credential stored on the device (entered in the
  desktop application, encrypted with the device's own key, as today).
- The agent validates the document, applies it atomically, persists it, and answers `ConfigApplied`.
  An invalid document is rejected with `success=false` and the previous configuration stays active.
- First enrollment: the cloud's default document is created from the tenant defaults. If the agent already
  has local monitor points (existing installation), it reports them in the full `MonitorPointReport`; the
  cloud imports them into the device's configuration (version 1, `Origin=Agent`) so nothing is lost.
- From then on the cloud version wins on a managed device; the desktop application shows settings read-only
  with "Managed by Monitor Cloud".

## 9. Commands (milestone M11)

Types: `refresh-inventory`, `run-speed-test`, `restart-agent`, `service-start`, `service-stop`,
`service-restart`. Each requires the plan feature `remote.actions`, the permission `devices.manage`, and
`features.remoteActions = true` in the device configuration. The cloud signs every command with an ES256 key
(`Commands:SigningKey`), the agent verifies with the public key received at enrollment, checks `expires_at`
(max 5 minutes), the `nonce` (kept 10 minutes) and that the signed device id is its own. Results are audited.
A local setting on the device (`Cloud:AllowRemoteActions=false`) refuses all commands regardless of the cloud.

## 10. Agent changes (work package AG, repository `UBGMonitor`)

| Id | Change |
|---|---|
| AG-0 | Move all projects to `net10.0` (.NET 8 support ends 10 November 2026). Fix build warnings introduced by the upgrade. |
| AG-1 | Remove `LicensingClient` credentials from `appsettings.json`; the agent no longer calls the Licensing Platform. Rotate the exposed secret in the Licensing Platform (manual step for the owner). |
| AG-2 | Stable identity: persist the installation id; use `DeviceFingerprint` as `fingerprint`. |
| AG-3 | New project `MonitorAgent.Cloud` referenced by the service: `CloudOptions`, `EnrollmentClient`, `DeviceTokenProvider`, `GatewayConnection` (gRPC), `MessagePump`, `Outbox`, `MinuteAggregator`, `IssueChangeTracker`, `InventoryPublisher`, `ConfigApplier`, `LiveModeController`. |
| AG-4 | Protocol package: copy `agent.proto` under `proto/` with `proto/VERSION` (sha256); `scripts/sync-proto.ps1` refreshes it; a test fails when the hash differs from the recorded one. |
| AG-5 | Outbox in the existing SQLite database: table `outbox (seq INTEGER PRIMARY KEY AUTOINCREMENT, message_id TEXT, kind TEXT, payload BLOB, created_utc INTEGER, sent_utc INTEGER NULL)`; rows deleted on `Ack`; size cap 200 MB, dropping oldest `metric` rows first, never `issue`, `config` or `command` rows. |
| AG-6 | `MinuteAggregator`: samples every `sampleSeconds`, produces min/max/avg/P95 per minute (replaces the single instantaneous sample for cloud purposes; the local reports keep working). |
| AG-7 | Licence handling: `LicenseService` gets its token from enrollment and `LicenseUpdate`; offline verification stays. Fix the parser: device binding from `sub`, licence expiry from `lic_exp`. The desktop "Activate" action calls the service, which calls `enroll`. |
| AG-8 | Thresholds: one evaluator fed by the cloud document (duration + hysteresis); remove the second threshold path. |
| AG-9 | Local API: loopback only when enrolled; settings endpoints read-only in managed mode. |
| AG-10 | Reconnect: exponential back-off 1 s -> 5 min with +/-20% jitter; honour `retry_after_seconds`; proxy from system settings. |
| AG-11 | Installers accept `PRODUCTKEY`, `LOCATION` and `CLOUDURL` (MSI properties; environment variables `MONITORAGENT_PRODUCTKEY` etc. for deb/pkg) and enroll on first start. |
| AG-12 | Tests: aggregator maths, outbox ordering and cap, issue change tracking, config validation, token parser, and an end-to-end test against `MonitorCloud.GatewayTests`' in-memory server. |

`Cloud` settings on the agent: `BaseUrl`, `GatewayUrl` (from enrollment), `AllowRemoteActions`,
`Enabled`. Enrollment state (`deviceId`, protected `deviceSecret`) is stored next to the licence file with
the existing `SecretProtector`.

## 11. Device simulator (`tools/MonitorCloud.DeviceSimulator`)

A console tool that speaks the real protocol, used for demos, integration tests and load tests.

```
simulator enroll --count 20 --tenant acme --location cairo-hq        # Fake licensing: uses seed product keys
simulator run --devices 50 [--tenant acme] [--scenario normal|cpu-spike|disk-fill|flapping|offline-wave]
simulator load --devices 1500 --duration 10m --report out/load.json
```

It generates plausible metrics (diurnal CPU curve with noise per device seed), minute batches, snapshots with
top-5 lists, inventory documents, monitor points, and issue events when values cross the thresholds of the
configuration it received. Its core is a library (`SimulatedAgent`) reused by `MonitorCloud.GatewayTests`.

## 12. Hosting note

gRPC needs HTTP/2 end to end. Before milestone M4 is accepted, deploy the gateway to the intended host and
run `simulator run --devices 1` from outside its network. If the host or its reverse proxy cannot pass
HTTP/2 streams (common on shared IIS hosting), stop and raise an ADR: the fallback is a WebSocket transport
carrying the same Protobuf envelopes behind the existing `IAgentTransport` abstraction.
