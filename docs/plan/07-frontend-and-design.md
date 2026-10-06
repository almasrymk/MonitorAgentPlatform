# 07 - Frontend and design

## 1. Design sources

| File | Screen |
|---|---|
| `docs/design/00-menu-structure.png` | Admin menu, client menu, device console menu |
| `docs/design/01-platform-admin-dashboard.png` | Platform Admin Dashboard |
| `docs/design/02-customers.png` | Customers |
| `docs/design/03-customer-workspace.png` | Customer workspace (customer overview) |
| `docs/design/04-location-overview.png` | Location overview |
| `docs/design/05-location-devices.png` | Location devices |
| `docs/design/06-device-details.png` | Device details ("Device Console") |
| `docs/design/07-customer-screens-overview.png` | Low-resolution overview of the customer-side screens |
| `docs/design/tokens.scss` | Colours, type, spacing |

Build each screen to match its image at 1672 x 941: same blocks, same order, same labels, same colours.
For screens that exist only in the low-resolution overview, follow the written specification in section 5 and
reuse the components of the full-size screens.

### Deliberate corrections to the mockups

Implement these instead of copying the mockup literally:

1. **Sample data.** Never copy names, IP addresses, network names or monitor-point names from the images.
   Seed data comes from 08.
2. **Totals are consistent.** Status tiles and charts on one screen always add up (state model, 00 D14).
   A device is counted once in health: `Healthy`, `Warning`, `Critical` or `Offline` (= health `Unknown`).
3. **Trend colours follow meaning.** Each tile declares `goodWhen: 'up' | 'down'`. Fewer offline, warning,
   critical or unlicensed devices is green; more is red. The arrow shows direction.
4. **One time range per screen.** The top-bar date range drives every widget. A widget with its own
   selector (e.g. "Last 7 days") shows that choice in its header and ignores the global range.
5. **Device cards show stored values** (`DeviceStates`), with "Last seen". Live values only on the device
   screen. Default sort is by severity.
6. **Suspend and Archive on customer cards** open a confirmation dialog that requires a reason. Open
   Workspace also asks for a reason (03 section 5).
7. **Plans is read-only** (04 section 5). **Upgrade Plan / Manage Subscription** open a "Contact your
   account manager" dialog.
8. **Version text** in the sidebar footer comes from the build (`Monitor Agent Platform v{version}`).
9. **Times** are shown in the location's time zone (tenant time zone on cross-location screens) with the
   zone in a tooltip.
10. **Remote Actions** is visible only when the plan feature, the permission and the device setting allow
    it; otherwise the button is not rendered.
11. **Resolved is a status, not a severity.** "Incidents by Severity" shows Critical, Warning and Info in
    the donut and the resolved count as a caption under the legend.
12. **Unlicensed devices** show a banner on the device screen and lock the actions listed in 00 D19.

## 2. Technology

| Item | Choice |
|---|---|
| Angular | The same major version as the Licensing Platform portal (21), or 22 if the installed Node supports it. Standalone components, signals, new control flow, typed forms, `provideHttpClient(withFetch())` |
| Styles | SCSS, tokens as CSS custom properties, logical properties only, no UI component library |
| Charts | Apache ECharts through one thin `mc-chart` wrapper, lazy-loaded; ring gauges and progress bars are SVG/CSS components |
| Realtime | `@microsoft/signalr` in `LiveService` |
| API types | Generated from `/openapi/v1.json` with `openapi-typescript` into `core/api/schema.d.ts` (`npm run api:types`); CI fails when the file is stale |
| i18n | `I18nService` with `en.json` and `ar.json` dictionaries (same approach as the Licensing portal), `dir` set on `<html>` |
| Tests | Angular's default unit-test runner for the chosen version; Playwright for e2e |
| Dev server | Port 4300, proxy `/api`, `/hubs`, `/openapi` to the API on `http://localhost:5300` |

Budgets (`angular.json`): initial JS <= 350 KB gzip warning / 450 KB error; any lazy chunk <= 250 KB gzip
except the ECharts chunk (<= 350 KB).

## 3. Application structure

```
portal/src/app/
  core/
    api/            schema.d.ts (generated), ApiClient, typed endpoint services per module
    auth/           AuthService (signals), token storage, guards (auth, permission, area), interceptors
    live/           LiveService (SignalR), subscription helpers
    i18n/           I18nService, dictionaries
    layout/         AppShell, Topbar, Sidebar (contextual), Breadcrumb, WorkspaceBanner
    state/          ScopeStore (area, tenant, location, device), DateRangeStore, ThemeStore
  shared/
    ui/             design-system components (section 4)
    charts/         mc-chart, TrendChart, DonutChart, Sparkline, RingGauge, SpeedGauge, HBar
    pipes/          relativeTime, bytes, duration, percent, localTime
  features/
    auth/           login, accept-invitation
    platform/       dashboard, customers, plans, notifications, archive, reports, users, settings
    workspace/      overview, locations, subscription, users, settings, reports, notifications, archive
    location/       overview, devices, notifications, archive, reports, settings
    device/         overview, monitor-points, applications, reports, settings, about
```

`workspace`, `location` and `device` features are shared by both audiences. The scope comes from the route:

| Area | Route prefix | Who |
|---|---|---|
| Platform | `/admin/...` | platform roles |
| Customer workspace opened by platform staff | `/admin/customers/:tenantId/...` | platform roles (sends `X-Tenant-Id`) |
| Customer | `/app/...` | tenant roles |

Routes (the same child routes under both prefixes where marked *):

```
/login
/admin/dashboard | customers | plans | notifications | archive | reports | users | settings
/admin/customers/:tenantId/*            /app/*
   overview (customer dashboard)
   locations
   locations/:locationId/overview | devices | notifications | archive | reports | settings
   devices                                (all devices; client menu "Devices")
   devices/:deviceId/overview | monitor-points | applications | reports | settings | about
   subscription
   reports | notifications | archive | users | settings
```

The sidebar is contextual, exactly as in the designs:

| Context | Items |
|---|---|
| Platform | Dashboard, Customers, Plans, Notifications (badge), Archive, Reports, Users & Roles, Settings |
| Customer (client) | Dashboard, Locations, Devices, Subscriptions, Reports, Users, Archive, Settings |
| Location | Overview, Devices, Notifications (badge), Archive, Reports, Settings |
| Device | back link "Customers" / "Devices", Overview, Monitor Points, Applications, Reports, Settings, About |

Rules: guards check permissions from the token; a hidden menu item's route is also blocked; HTTP
interceptors add the bearer token, refresh once on 401, add `X-Tenant-Id` inside a workspace, add
`X-Correlation-Id`, and turn problem details into toasts (`code` -> translated message).
Access token in memory, refresh token in `sessionStorage`; on reload the app refreshes silently.

## 4. Design system components (`shared/ui`)

Each is standalone, `OnPush`, has inputs only (no service calls), a story-like demo page under
`/dev/components` (development builds only) and unit tests.

`Card`, `KpiTile` (icon tile, label, value, delta with `goodWhen`, caption), `StatusPill`
(online/offline/active/suspended/expiring/licensed/unlicensed), `SeverityDot` + `SeverityLabel`,
`CountBadge`, `Button` (primary-solid, primary-outline, secondary, danger-outline, success-outline, ghost,
icon), `SearchInput`, `Select`, `DateRangePicker`, `Tabs`, `ViewToggle` (grid/list), `DataTable` (sortable
headers, server paging, sticky header, virtual scroll, empty and loading states), `Pagination`,
`ProgressBar`, `HealthBar`, `Avatar`, `OsIcon`, `CopyButton`, `Breadcrumb`, `PageHeader`,
`EntityHeader` (image/icon, title, pill, meta row, actions), `Menu` (kebab), `Dialog`,
`ConfirmWithReasonDialog`, `Drawer`, `Toast`, `Skeleton`, `EmptyState`, `ErrorState`, `Accordion`,
`CustomerCard`, `LocationCard`, `DeviceCard`, `AlertRow`, `Timeline`.

Every data view has four states: loading (skeleton shaped like the content), empty (icon, sentence, primary
action when the user may create), error (message + retry), content.

Accessibility: keyboard reachable, visible focus ring (`--mc-focus-ring`), status never by colour alone
(dot + text), charts have a text summary in `aria-label`, contrast AA on the dark theme.

## 5. Screens

For each screen: route, the request(s) it makes, and its blocks from top to bottom, left to right.

### 5.1 Platform Admin Dashboard - `/admin/dashboard` - `GET /platform/dashboard`

1. Page title "Platform Admin Dashboard", subtitle "Overview of your Monitor Agent platform, customers,
   devices and subscriptions."
2. Eight KPI tiles: Total Customers, Total Devices, Healthy Devices, Warning Devices, Critical Devices,
   Licensed Devices, Unlicensed Devices, New Devices. Each: value, delta count and % against the previous
   30 days, caption ("vs last 30 days" or "% of total").
3. Row of three cards: **Incident Trend** (area lines Critical / Warning / Info, selector Last 7 / 30 days),
   **Incidents by Severity** (donut with total in the centre; legend Critical, Warning, Info with count and
   %; selector Last 30 days; caption "Resolved: n"), **Subscription
   Distribution** (donut "N Customers"; Enterprise, Business, Professional, Starter).
4. Row of three cards: **Top Customers by Device Count** (#, Customer, Devices, Online, Health bar + %),
   **Expiring Subscriptions** (Customer, Plan, Expires In, Status pill Expiring / Warning / Active),
   **Recent Alerts** (Time, Severity, Customer, Message). Each has "View All".
5. Row of three cards: **Device Health Overview** (donut, Healthy / Warning / Critical / Offline),
   **Devices by Operating System** (horizontal bars Windows, Linux, macOS, Other with count and %),
   **Recent Activity** (timeline from the audit log: device registered, user login, plan updated, device
   archived, user added).

Live: `SubscribePlatform()`; refetch on `summaryChanged`.

### 5.2 Customers - `/admin/customers` - `GET /platform/tenants/summary`, `GET /platform/tenants`

1. Title "Customers", subtitle "Manage and monitor your customers, their devices, subscriptions and health
   status."
2. Four tiles: Total Customers, Active Customers, Expiring Soon, Suspended.
3. Toolbar: search ("Search customers by name, domain or contact..."), All Plans, All Health Status, All
   Subscription Status, Sort by, grid/list toggle.
4. Grid of `CustomerCard` (4 per row at 1672 px): avatar letter, name, plan, status pill, locations count,
   devices count, Healthy / Warning / Critical counts, Health Score ring, License Usage "used / limit" bar,
   Next Renewal, buttons **Open Workspace**, **Archive**, **Suspend** (or **Reactivate** when suspended),
   kebab menu (Edit, View audit).
5. List view: the same data as a table. Server paging, 24 cards per page.

### 5.3 Customer workspace / Customer dashboard - `.../overview` - `GET /dashboard`

1. Breadcrumb; entity header (icon or logo, name, Active pill, "Plan | N Locations | N Devices | Customer
   since"), Actions menu.
2. Eight tiles: Locations, Total Devices, Online, Healthy, Warning, Critical, Licensed, Unlicensed.
3. **Locations Overview**: horizontal `LocationCard`s (image, name, Online pill, city, Devices / Healthy /
   Warning / Critical), "View All Locations".
4. Row: **Locations Health** donut ("N Devices"; Healthy, Warning, Critical, Offline), **Incident Trend**,
   **Top Problematic Devices** (Device Name, Location, Issue, Status).
5. Row: **Device Status by Location** table (Location, Total, Online, Healthy, Warning, Critical, Health %
   bar), **Recent Alerts**, **License Summary** (donut Licensed / Unlicensed, plan name, "used / limit
   devices" bar, "Renews on").

For tenant roles the same component is the **Customer Dashboard** (`/app/overview`); the platform sidebar
is replaced by the client sidebar.

### 5.4 Locations - `.../locations` - `GET /locations`

Title "Locations", subtitle "Manage your locations, devices and their health status.", "+ Add Location".
Grid of `LocationCard` (vertical variant): image, name, city/country, Online pill, four counts (Devices,
Online, Warning, Critical), health ring %, three small bars. Click opens the location overview.
Add/Edit location in a drawer: name, code, country, city, address, time zone, contact, image.

### 5.5 Location overview - `.../locations/:id/overview` - `GET /locations/{id}/dashboard`

1. Breadcrumb; entity header (image, name, Online pill, customer, city, plan, devices, customer since),
   Actions (Edit location, Add device, Generate report).
2. Six tiles: Total Devices, Online, Healthy, Warning, Critical, Licensed.
3. Row: **Incident Trend**, **Devices by Operating System** donut, **Device Health** donut (Healthy,
   Warning, Critical, Offline).
4. Row: **System Resource Averages** (four ring gauges: CPU Usage green, RAM Usage blue, Disk Usage
   purple, Health Score green; caption "Across N online devices"), **Top Problematic Devices** (Device
   Name, Issue, Location, Severity, Last Seen).
5. Row: **Recent Alerts** (Time, Severity, Device, Message, Location), **Location Summary** (Address,
   Contact, Last Sync with "Synced successfully", image, Edit).

Live: `SubscribeLocation(id)`.

### 5.6 Location devices - `.../locations/:id/devices` - `GET /devices/summary`, `GET /devices`

1. Entity header as 5.5.
2. Six tiles: Total Devices, Online, Offline, Licensed, Warning, Critical.
3. Toolbar: search ("Search devices by name, IP, or tag..."), All Operating Systems, All Statuses, All
   License Status, Sort by, grid/list toggle. "+ Add Device" opens the enrollment dialog (creates an
   enrollment code and shows the install command for Windows, Linux and macOS with copy buttons).
4. Grid of `DeviceCard` (4 per row): OS icon, name, OS, IP, status dot + text, Licensed / Unlicensed pill,
   open-alert count badge coloured by the highest severity, kebab (Rename, Move, Unlicense, Retire); CPU
   (green), RAM (blue), Disk (purple) bars with %; Last seen, Uptime; buttons **Open Console** (disabled
   when offline) and **Details**. Both open the device screen (Open Console requests live mode).
5. **Recent Device Alerts** table (Time, Severity, Device, Message, Operating System, Location), "View All
   Alerts".

Paging 24 per page; list view is a table with the same columns. Live: `deviceStateChanged` updates cards in
place.

### 5.7 Device details - `.../devices/:id/overview` - `GET /devices/{id}`, `/overview`, `/metrics`, `/disks`

1. Breadcrumb; header: device icon, name + rename, Online pill, customer | location, OS; fields IP Address
   (copy), Last Seen (absolute + relative), Uptime, Agent Version, Customer (link), Location (link);
   **Remote Actions** (when allowed); kebab.
2. Row: **Service Status** (Running / Stopped, message, uptime, RAM), **Monitor Points** (carousel of round
   icons with name and status dot; selecting one shows its detail in a popover), **Messages & Issues
   Board** (severity icon, coloured title, description, time; "View All").
3. Row of four metric cards, each: title with a check when healthy, ring gauge with the current value, a
   key-value list, a history area chart for the global date range, and a "Top 5" table with bars.
   - **CPU Usage** (green): Usage, Cores, Speed, Temp, Processes, Model; Top 5 by CPU (Process, PID, CPU).
   - **RAM Usage** (blue): Usage, Total, Used, Free, Cached; Top 5 by RAM (Process, PID, Memory).
   - **Disk Usage** (purple): Active, Read, Write, Response, Used, Free; Top 5 by Disk (Process, PID, I/O).
   - **Internet & Network**: speed gauge, adapter, Download, Upload, Speed test, Public IP, Local IP, Ping,
     Loss; Top 5 by Network (Process, PID, Network).
4. Row: **Disk Status** table (Drive, Label, File System, Total, Used, Free, Usage bar, Status with %),
   **Hardware & OS** accordion (Basic Hardware, Extended Hardware, Operating System, Network
   Configuration; "View Details").

Live: `SubscribeDevice(id)`; `POST live-sessions` on open and every 30 s while the tab is visible; gauges
and "now" values update from `liveSample`, lists from `/overview` on `snapshotUpdated`. When the device is
offline the screen shows the last snapshot with an "Offline since ..." banner and stops live requests.

Other device tabs:

| Tab | Content | Requests |
|---|---|---|
| Monitor Points | Table (Name, Type, Target, Status, Response, Last checked, Interval, Enabled); add/edit drawer by type; delete | `/devices/{id}/monitor-points` |
| Applications | Sub-tabs Programs, Services, Users (tables with search) | `/devices/{id}/inventory/{kind}` |
| Reports | Report types filtered to this device, recent reports | `/reports*` |
| Settings | Thresholds and intervals form, applied vs target version, licence state, Unlicense / Retire | `/devices/{id}/configuration` |
| About | Agent version, protocol version, enrolled at, fingerprint (masked), device id, last config applied | `/devices/{id}` |

### 5.8 Customer-side screens (from the overview image)

| Screen | Route | Blocks |
|---|---|---|
| Subscription & Licenses | `.../subscription` | Tabs Overview, All Devices (n), Licensed (n), Unlicensed (n). Overview: **Current Plan** (plan name, Active pill, "used / limit devices" bar, "Renews on", Upgrade Plan), donut Licensed / Unlicensed, **License Usage by Operating System** (bars with %), **Upcoming Renewal** (date, days remaining, Manage Subscription). Device tabs: device table filtered by licence state |
| Users & Permissions | `.../users` | Tabs All Users (n), Administrators (n), IT Managers (n), Technicians (n); "+ Add User"; table Name (avatar), Email, Role pill, Permissions, Status, Last Login; row menu Edit, Deactivate, Resend invitation |
| Reports | `.../reports` | Three columns: **Report Type** list (Overview Report, Location Summary, Device Health, Performance (CPU/RAM/Disk), Network Usage, Alerts & Incidents, License Usage, Custom Report), **Report Options** (Locations, Devices, Time Range, Group By, Generate Report), **Recent Reports** (title, date, format, size, download) |
| Settings | `.../settings` | Tabs General, Alert Settings, Monitoring, Locations, Integrations. Alert Settings: **Alert Channels** (Email Notifications, SMS Notifications, In-App Notifications, Webhook Integration switches with captions), **Alert Recipients** (table Name, Email, Events; "+ Add Recipient"; delete). SMS shows "Not available yet" and stays off |
| Notifications | `.../notifications` and location-level | Filters All Severities, Last 7 days; table Time, Severity, Device, Message, Category; mark as read |
| Customer Archive | `.../archive` | Tabs Company Information, Contacts, Remote Access (platform roles only), Notes, Files & Attachments. Company Information: **Company Details** (Company Name, Industry, Website, Phone, Address, Customer Since, Account Manager, Status; Edit) and **Recent Files** with download |
| Devices (all) | `/app/devices` | Same as 5.6 without the location header, plus a Location filter |

Platform-level Notifications, Archive, Reports, Users & Roles and Settings reuse the same components with
the platform scope (cross-customer lists get a Customer column).

## 6. Login

Centred card on the page background: logo, product name, e-mail, password, "Sign in", language switch.
Errors under the form (`AUTH_INVALID_CREDENTIALS`, `AUTH_LOCKED` with minutes left,
`AUTH_TENANT_SUSPENDED`). After sign-in platform roles go to `/admin/dashboard`, tenant roles to
`/app/overview`.

## 7. Charts

| Chart | Options |
|---|---|
| TrendChart | Smooth lines with gradient area fill, points on hover, legend on the top right, y-axis from 0, x-axis by day in the scope's time zone; series colours danger / warning / info |
| DonutChart | Thickness 22%, centre value + label, legend rows: dot, name, count, % |
| RingGauge | SVG, 270-degree track, value in the centre, colour by series |
| SpeedGauge | 180-degree gauge with ticks, value in Mbps |
| Sparkline / history | Area chart without axes labels except start and end dates |
| HBar | CSS bar with label, value and % |

No animation longer than 300 ms; `prefers-reduced-motion` disables animation; charts resize with a
`ResizeObserver`.

## 8. Definition of done for a screen

- Matches its design image side by side at 1672 x 941 (blocks, order, labels, colours, spacing within 4 px).
- Works at 1280 px and 1920 px; no horizontal scroll at 1280 px.
- All four data states implemented; permissions hide and block what the role cannot do.
- English and Arabic, LTR and RTL checked.
- Unit tests for the component logic, one Playwright test for the main flow, a Playwright screenshot
  baseline in dark theme.
