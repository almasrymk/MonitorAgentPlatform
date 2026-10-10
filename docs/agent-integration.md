# Connecting Monitor Agent to Monitor Cloud

This guide covers four tasks (MC-702):

- pointing an installed Monitor Agent at a Monitor Cloud environment;
- enrolling it in a location;
- checking that it works;
- troubleshooting.

The connector is the `MonitorAgent.Cloud` project in the agent repository (`almasrymk/UBGMonitor`). The protocol is
`proto/monitor/agent/v1/agent.proto`; both repositories keep the same file, and its sha256 is recorded in the agent's
`proto/VERSION`.

## 1. What you need

| Item | Where it comes from |
|---|---|
| Cloud address | Your environment, e.g. `https://cloud.example.com` (Development: `http://localhost:5300`) |
| Gateway address | Returned by enrollment (`Agent:GatewayUrl` of the cloud). Development: `http://localhost:5301` |
| Product key | The customer's licence in the Licensing Platform. Demo keys are listed in `seed/README.md` |
| Location code (optional) | Location -> Add Device -> Create code (`LOC-XXXXXX-XXXXXX`, valid 24 h by default). Without a code the device goes to the customer's default location ("Unassigned") |

## 2. Point the agent at the cloud

The connector is **off** until it has a cloud address. An agent without one keeps working locally as before.

You can set the values in two ways.

**Installer or environment** (AG-11). The service reads these variables:

```
MONITORAGENT_CLOUDURL   = https://cloud.example.com
MONITORAGENT_PRODUCTKEY = XXXXXX-XXXXXX-XXXXXX-XXXXXX-XXXXXX
MONITORAGENT_LOCATION   = LOC-XXXXXX-XXXXXX
```

Setting `MONITORAGENT_CLOUDURL` turns the connector on.

**`appsettings.json`** of the service:

```json
"Cloud": {
  "Enabled": true,
  "BaseUrl": "https://cloud.example.com",
  "GatewayUrl": "",
  "LocationCode": ""
}
```

`GatewayUrl` overrides the address returned by enrollment. Use it only for tests or proxies.

The product key is used once, to enroll. It is never written back to the settings. Restart the service after you
change these values.

## 3. Enrollment and what is stored

On the first start the agent calls `POST /api/agent/v1/enroll` with three values:

- the product key;
- its fingerprint (`ma-` and 32 hex digits, from the machine id; the same value is the Licensing device id);
- its host facts.

The cloud does the following:

1. It activates a seat in the Licensing Platform.
2. It places the device in the location of the code.
3. It returns the device id, a device secret, the gateway address and the public command-signing keys
   (`commandSigningKeys`, a JWK set; remote actions, section 5a).

The agent stores them in `cloud.json` in its state folder:

| System | State folder |
|---|---|
| Windows | `C:\ProgramData\MonitorAgent` |
| Linux | `/var/lib/monitoragent` |
| macOS | `/Library/Application Support/MonitorAgent` |

The device secret is encrypted with the agent's `SecretProtector`; the file never holds it in clear text. The
outbox and the connector's own state are in `cloud.db` in the same folder.

Enrolling the same machine again (same fingerprint) re-activates the same device in its previous location. The
same happens after a reinstall or after a device was retired. Enrolling one fingerprint is limited to 5 times per
hour.

## 4. Check that it works

1. **Agent log.** The log is `agent-.log`. These lines show a healthy start:

   ```
   [Cloud] Enrolled as device 0199... (Acme Corporation / Cairo HQ)
   [Cloud] Connected (session 0199...), 3 messages waiting
   ```

2. **Portal.** Open Location -> Devices. The device appears Online within a few seconds. On its device screen:
   - the gauges move every 2 s while the screen is open (live mode);
   - the history charts fill one point per minute;
   - Applications lists programs and services.
3. **Alerts.** A problem the agent detects becomes an alert on the device and changes its health; it is resolved
   when the agent clears it. Example: a disk above its threshold gives `disk-C`.

## 5. Offline behaviour

- **Without the cloud:** the agent keeps monitoring and keeps its local reports. Minute aggregates, problems and
  inventory wait in the outbox. The outbox is capped at 200 MB; above that the oldest minute rows are dropped
  first, and problems are never dropped.
- **Reconnecting:** the agent waits 1 s, 2 s, 4 s and so on, up to 5 minutes, with +/-20 % jitter. It honours the
  cloud's `retry_after_seconds`.
- **Backlog:** after reconnecting, the cloud tells the agent the last sequence it stored, and the agent resends
  everything after it, oldest first. The minute history therefore has no gaps after an outage. Minutes older than
  the cloud's retention (30 days) are acknowledged and dropped.

## 5a. Remote actions (M11)

From the device screen an administrator can send six actions: `refresh-inventory`, `run-speed-test`,
`restart-agent`, `service-start`, `service-stop`, `service-restart` (the service ones with a service name). The
portal shows the Remote Actions button only when the plan includes remote actions, the user has `devices.manage`,
the device is licensed and **Allow remote actions** is ticked in the device's Settings tab. Every action needs a
reason; requests, results and expiries are in the audit log.

**On the agent they are off by default.** Turn them on per machine:

```json
"Cloud": { "AllowRemoteActions": true }
```

With `false` the agent refuses every command, whatever the cloud says.

The agent checks each command before it runs it, in this order:

1. the local switch;
2. the key id is one of the `commandSigningKeys` received at enrollment;
3. the ES256 signature (64 bytes, r then s) over the UTF-8 text
   `command_id|type|parameters_json|expires_at|nonce|device_id`, with both ids as 32 hex digits, `expires_at` as Unix
   milliseconds and the agent's **own** device id (a command for another device fails here);
4. not expired, and the expiry at most 5 minutes (+30 s clock skew) ahead;
5. the nonce was not seen in the last 10 minutes (kept in `cloud.db`, so also across restarts).

A refusal is sent back as `REJECTED` (or `EXPIRED`) with the reason, for example `Invalid signature.`. Service
names must be plain names (`W3SVC`, `nginx`, `com.example.daemon`); they go to `ServiceController`, `systemctl` or
`launchctl` as one argument, never through a shell. `restart-agent` exits the service with code 1 after 5 s and the
service manager starts it again. The agent's own service cannot be stopped remotely.

A command that the device does not answer is marked Expired one minute after its 5-minute expiry. An offline device
gets pending commands right after it reconnects, if they have not expired.

Agents enrolled before M11 have no keys and refuse commands ("Unknown signing key") until they enroll again.
## 6. Troubleshooting

### Enrollment errors

The log shows `[Cloud] Enrollment refused: <code>`.

| Code | Meaning | What to do |
|---|---|---|
| `LIC_INVALID_LICENSE` | Unknown product key | Check the key |
| `LIC_SUSPENDED`, `LIC_REVOKED`, `LIC_EXPIRED` | The licence cannot be used | Contact the account manager |
| `LIC_SUBSCRIPTION_EXPIRED`, `LIC_SUBSCRIPTION_INACTIVE` | The customer's subscription is not active | Renew the subscription |
| `LIC_ACTIVATION_LIMIT_REACHED` | All seats are used | Release a seat (retire or unlicense a device) or add seats |
| `ENROLL_INVALID_LOCATION_CODE` | The code is wrong, expired, used up or revoked | Create a new code |
| `ENROLL_TENANT_NOT_ACTIVE` | The customer is suspended or archived | Platform staff |
| `ENROLL_PROTOCOL_UNSUPPORTED` | The agent is too old or too new for this cloud | Update the agent |
| `LICENSING_UNAVAILABLE` | The Licensing Platform did not answer | Nothing; the agent retries |
| `RATE_LIMITED` | Too many enrollments from this address or fingerprint | Nothing; the agent waits `Retry-After` |

The agent retries `LIC_*` refusals every 15 minutes. Other errors are retried with the reconnect back-off.

### Token and stream errors

| Symptom | Cause | What happens |
|---|---|---|
| `DEVICE_INVALID_CREDENTIAL` when getting a token | The device was retired, its credential revoked, or the secret is wrong | The agent forgets its secret and enrolls again with the same fingerprint |
| `Disconnect DUPLICATE_SESSION` | A second process or a cloned machine uses the same identity | Three replacements in 5 minutes raise a "Possible cloned device" alert |
| `Disconnect SESSION_ROTATE` | The cloud ends every stream after 12 hours | Reconnects after 1-10 s; normal |
| `Disconnect SERVER_SHUTDOWN` | The cloud is restarting | Reconnects after 5-30 s |
| `Disconnect DEVICE_RETIRED` / `CREDENTIAL_REVOKED` | Retired in the portal | Tries again after 10 minutes (enrolls again when it has a product key) |
| `Disconnect TENANT_ARCHIVED` | The customer was archived | Tries again after 10 minutes |
| Enrolled, but the log repeats `[Cloud] Stream ended: ...` and never `Connected` | HTTPS works, gRPC (HTTP/2) to the gateway is blocked by a proxy or firewall | Allow HTTP/2 to the gateway address |
| `clock-skew` alert on the device | The device clock is more than 5 minutes off | Fix the time sync; the alert resolves itself |

### Logs and files

- **Agent:** `agent-.log` in the log folder:
  - Windows: `C:\ProgramData\MonitorAgent\logs`
  - Linux: `/var/log/monitoragent`
  - macOS: `/Library/Logs/MonitorAgent`

  All connector lines start with `[Cloud]`.
- **Cloud:** each request is logged with its correlation id. The gateway logs sessions by device id.
- **Starting again:** stop the service, delete `cloud.json` and `cloud.db`, then start it with a product key. The
  device enrolls again; with the same fingerprint it is the same device.

## 7. Development and tests

- Run the cloud locally:

  ```
  dotnet run --project src/MonitorCloud.Api --launch-profile http
  ```

  REST is on port 5300 and the gateway on 5301 (ADR 0005).
- Run an agent against it without installing it:
  - set `MONITORAGENT_HOME` to a temporary folder, so it keeps its state away from an installed agent;
  - set `MONITORAGENT_CLOUDURL=http://localhost:5300`, `Cloud__GatewayUrl=http://localhost:5301`, a demo product
    key and a location code;
  - start `MonitorAgent.Service.dll` with `dotnet`.
- The agent's connector tests are in `tests/MonitorAgent.Tests/CloudTests.cs`. The end-to-end test runs when
  `MONITORCLOUD_E2E_URL`, `MONITORCLOUD_E2E_GATEWAY` and `MONITORCLOUD_E2E_PRODUCTKEY` (and optionally
  `MONITORCLOUD_E2E_LOCATION`) point at a running cloud.
- The cloud's `tools/MonitorCloud.DeviceSimulator` speaks the same protocol, for load tests and portal flows.
