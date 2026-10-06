# 08 - Seed data

Two seeders in `Infrastructure/Seed`, both idempotent:

| Seeder | Runs when | Creates |
|---|---|---|
| `BootstrapSeeder` | every environment, at start-up | Nothing unless `Seed:AdminEmail` and `Seed:AdminPassword` are set and no platform user exists: then the first Platform Admin |
| `DemoSeeder` | `Seed:DemoData = true` (Development default; **refuses to run** when the environment is Production) | Everything below |

`dotnet run --project src/MonitorCloud.Api -- seed --reset` drops the demo data and seeds again.
All generated values come from `new Random(20261006)` so every run produces the same data; all dates are
relative to the moment of seeding.

Rules for all seed and test data: company names from the table below only; e-mail domains `*.test` and
`monitor.local`; local IPs from `192.168.0.0/16` and `10.0.0.0/8`; public IPs from `203.0.113.0/24` and
`198.51.100.0/24`; no real network names, host names or people.

## 1. Platform users

| E-mail | Password | Role | Name |
|---|---|---|---|
| `admin@monitor.local` | `Admin@12345` | Platform Admin | Admin User |
| `support@monitor.local` | `Support@12345` | Platform Support | Support User |

## 2. Plans (as returned by the fake Licensing gateway)

| Code | Name | Device limit | Features |
|---|---|---|---|
| `STARTER` | Starter | 50 | `monitoring`, `alerts`, `reports.basic` |
| `PROFESSIONAL` | Professional | 200 | Starter + `notifications.email`, `monitorpoints` |
| `BUSINESS` | Business | 250 | Professional + `reports.advanced`, `archive` |
| `ENTERPRISE` | Enterprise | 500 | Business + `notifications.webhook`, `remote.actions` |

## 3. Detailed customers

These eight have locations, devices, users, alerts and history. They mirror the shape of the designs.

| # | Customer | Code | Plan | Status | Locations | Devices | Renews in | Customer since |
|---|---|---|---|---|---|---|---|---|
| 1 | Acme Corporation | `ACME` | Enterprise | Active | 3 | 316 | 101 days | 2 years 9 months ago |
| 2 | Nile Trading Group | `NILE` | Business | Active | 3 | 198 | 47 days | 2 years ago |
| 3 | Delta Logistics | `DELTA` | Professional | Active | 4 | 142 | 65 days | 18 months ago |
| 4 | Horizon Retail | `HORIZON` | Enterprise | Active | 6 | 124 | 22 days (expiring soon) | 3 years ago |
| 5 | Sahara Foods | `SAHARA` | Business | Active | 2 | 96 | 131 days | 14 months ago |
| 6 | Gulf Engineering | `GULF` | Enterprise | Active | 8 | 428 | 28 days (expiring soon) | 4 years ago |
| 7 | Pyramid Pharma | `PYRAMID` | Professional | Active | 3 | 87 | 96 days | 1 year ago |
| 8 | Oasis Hospitality | `OASIS` | Starter | **Suspended** | 1 | 24 | subscription suspended | 8 months ago |

Plus **40 generated customers** ("Customer 09" ... "Customer 48", codes `C09`...`C48`): one or two
locations, 5-40 devices each, plan drawn with weights Enterprise 30%, Business 30%, Professional 25%,
Starter 15%; one of them `Suspended`; four more renewing within 30 days. They have devices and current
state but only hourly history for 24 hours and no users except one Administrator.

Platform totals on the dashboards are whatever this data adds up to. **Never hard-code a number from a
mockup.**

### Acme Corporation in detail

| Location | Code | City | Time zone | Devices | Online | Healthy | Warning | Critical | Offline | Unlicensed |
|---|---|---|---|---|---|---|---|---|---|---|
| Cairo HQ | `CAIRO-HQ` | Cairo, Egypt | Africa/Cairo | 142 | 134 | 126 | 6 | 2 | 8 | 4 |
| Alexandria Branch | `ALEX` | Alexandria, Egypt | Africa/Cairo | 96 | 91 | 77 | 10 | 4 | 5 | 9 |
| Dubai Office | `DUBAI` | Dubai, UAE | Asia/Dubai | 78 | 73 | 44 | 20 | 9 | 5 | 13 |

Healthy + Warning + Critical = Online; Online + Offline = Devices. Operating systems at Cairo HQ: Windows 98,
Linux 28, macOS 12, Other 4; other locations use the ratio 69 / 20 / 8 / 3 %.

The first eight devices of Cairo HQ are fixed so the device screens always have known content:

| Name | OS | Local IP | Connection | Health | Licence | Open alerts |
|---|---|---|---|---|---|---|
| WEB-SRV-01 | Windows Server 2019 | 192.168.1.10 | Online | Critical | Licensed | High CPU usage (critical), 2 warnings |
| DB-SRV-01 | Ubuntu 22.04 LTS | 192.168.1.20 | Online | Warning | Licensed | Disk space low (warning) |
| APP-SRV-02 | Windows Server 2022 | 192.168.1.30 | Offline (6 hours) | Unknown | Licensed | Device offline (critical), Application not running |
| DESK-01 | macOS Sonoma 14.0 | 192.168.1.40 | Online | Healthy | Licensed | - |
| BR-DC-01 | Windows Server 2019 | 192.168.1.50 | Online | Warning | Licensed | Service response slow (warning) |
| SQL-DB-01 | CentOS 7 | 192.168.1.60 | Online | Critical | Licensed | Disk space critically low (critical) |
| DEV-MAC-01 | macOS Ventura 13.6 | 192.168.1.70 | Online | Warning | **Unlicensed** (`seat released`) | High memory usage (warning) |
| FILE-SRV-01 | Windows Server 2016 | 192.168.1.80 | Online | Healthy | Licensed | - |

Remaining device names are generated as `{PREFIX}-{NN}` with prefixes `WEB-SRV, APP-SRV, DB-SRV, FILE-SRV,
DC, DESK, LAP, POS, KIOSK, DEV-MAC` and a counter per prefix per location.

WEB-SRV-01 also gets: six monitor points (an FTP server application, Database, IIS, Windows Services,
Disk C:, Network - one of them `Critical`), three partitions (C: 92% Critical, D: 76% Warning, E: 45%
Healthy), a full inventory (hardware, OS, network, 40 programs, 60 services, 6 users) and a snapshot with
top-5 lists.

### Users of the detailed customers

For each: `admin@{code}.test` (Administrator), `it@{code}.test` (IT Manager), `tech@{code}.test`
(Technician), `viewer@{code}.test` (Report Viewer), all with password `Demo@12345`, e.g.
`admin@acme.test`. Acme has four more users so its Users screen shows 8: a second Administrator, two more IT
Managers (one restricted to Cairo HQ) and one inactive Technician. Oasis Hospitality's users exist but
cannot sign in (tenant suspended).

## 4. History

| Data | Scope | Amount |
|---|---|---|
| `MetricHours` | all devices of the eight detailed customers | 7 days |
| `MetricMinutes` | Acme / Cairo HQ | last 6 hours |
| `DiskUsageHours` | the eight fixed devices | 7 days, C: of SQL-DB-01 filling steadily |
| `LiveSnapshots` | Acme / Cairo HQ online devices | one each |
| Alerts | all detailed customers | open alerts consistent with the health counts; resolved alerts spread over 30 days so the trend charts and `AlertDailyStats` have a curve with a peak three days ago |
| Notifications | platform and each detailed customer | 25 each, 3 unread |
| Audit | platform | 60 records over 7 days (sign-ins, device registered, plan updated, device archived, user added) for "Recent Activity" |
| Generated reports | Acme | 4 finished reports with small real files |
| Archive | Acme | company profile, 3 contacts, 4 files, 2 notes (1 internal), 2 remote-access entries |
| Enrollment codes | Acme / Cairo HQ | one valid code, printed to the console at seed time |

Metric values come from a per-device generator: base load by device role, a daily sine curve, noise, and
plateaus that match the device's open alerts (WEB-SRV-01 CPU at 92% for the last 20 minutes).

## 5. Fake Licensing data

`seed/licensing-fake.json` is generated by the same code and loaded by `FakeLicensingGateway`: the plans, one
customer per tenant (`licensingCustomerId` = deterministic GUID), one subscription and one licence each with
the plan's device limit, `activeActivations` equal to the tenant's licensed devices, and one demo product
key per customer (`seed/README.md` lists them; format `XXXXXX-XXXXXX-XXXXXX-XXXXXX-XXXXXX`).
Device secrets of seeded devices are derived as `HMAC(Seed:DeviceSecretKey, deviceId)` so the simulator can
connect as seeded devices in Development; this derivation exists only in `DemoSeeder` and the simulator.

## 6. What you should see after seeding

- Sign in as `admin@monitor.local`: dashboard with 48 customers, Gulf Engineering and Acme Corporation at the
  top of "Top Customers by Device Count", six expiring subscriptions, two suspended customers.
- Open Workspace on Acme Corporation: 3 locations, 316 devices, 290 licensed, 26 unlicensed.
- Cairo HQ -> Devices: the eight fixed devices first (sorted by severity), APP-SRV-02 offline with a
  disabled Open Console button.
- `simulator run --tenant acme --location cairo-hq --devices 20`: those 20 devices switch to live data and
  their cards update without a refresh.
- Sign in as `viewer@acme.test`: read-only, no Users or Settings in the menu.
- Sign in as `admin@oasis.test`: rejected with "customer suspended".
