# ADR 0009 - Details of reports, archive and settings (M9)

- Status: Proposed (applied in M9, awaiting product-owner review)
- Date: 2026-10-09

## Context

MC-901 to MC-906 describe the reports, the media storage, the archive and the remaining settings in a few lines.
Several details had to be decided while building them. None changes an endpoint of 06; two add error codes and
one adds screens the designs do not show.

## Decisions

1. **PDF uses the browser of the host, not Playwright.** The server prints its print-ready HTML with a headless
   Chromium-based browser (`--headless=new --print-to-pdf`): Edge, Chrome or Chromium, or `Storage:ChromiumPath`.
   This avoids a Node/Playwright runtime on the server. Without a browser, `GET /reports/types` returns
   `pdfAvailable: false` and a PDF request answers 400 `REPORT_PDF_UNAVAILABLE`; CSV always works (MC-901).
2. **A location-restricted user gets reports of their locations only.** The request is checked against the user's
   location scope (another location: 404 `LOCATION_NOT_FOUND`), and the job generates the report in that scope.
   Such a user lists and downloads only the reports they requested, because other reports may cover locations
   outside their scope.
3. **CSV is safe to open in a spreadsheet.** Text cells that start with `=`, `+`, `-`, `@`, tab or carriage return
   get a leading apostrophe (CSV injection). Files are UTF-8 with a BOM so Arabic names display correctly.
4. **Upload errors follow 06 exactly:** 413 `UPLOAD_TOO_LARGE`, 415 `UPLOAD_TYPE_NOT_ALLOWED`. Two new codes cover
   the remaining cases: 400 `UPLOAD_EMPTY` and 400 `UPLOAD_NAME_INVALID` (a name with no file part, e.g. `../..`).
   Only the last segment of a client file name is kept, whatever the separator. `ErrorKind` gained
   `PayloadTooLarge` and `UnsupportedMediaType`. Storage paths come from the database row, never from input, and
   are checked to stay under `{Storage:Root}/media`.
5. **Remote access needs the archive feature as well as `archive.internal`.** It is part of the archive in 06. Its
   values are encrypted with ASP.NET Data Protection (purpose `MonitorCloud.StoredSecrets.v1`). Lists show
   `12••••89`. Every reveal is audited at once in its own transaction (`archive.remote_access_revealed`), even if
   the request fails afterwards. The reveal answer has `Cache-Control: no-store`. Data Protection keys are kept
   under `{Storage:Root}/keys` when a root is configured.
6. **The webhook signature includes a timestamp.** Each request carries `X-Monitor-Timestamp` (Unix seconds) and
   `X-Monitor-Signature: sha256=<hex HMAC-SHA256 of "{timestamp}.{body}">`, so a receiver can reject replays. The
   secret is stored encrypted and never returned (`hasSecret` only). Settings > Integrations has a Test button
   that posts a sample payload.
7. **Location tabs Archive, Reports and Settings (07 section 3).** The location Archive tab shows the remote-access
   entries of that location (platform staff) and the customer's files. The location Settings tab edits the
   location's details. The location Reports tab is the Reports screen with the location fixed and the
   location-level report types.
8. **The audit list is a screen without a menu item.** The menus of 07 have no Audit entry. The list is at
   `.../audit` (customer, `audit.read`) and `/admin/audit` (platform, with a Customer column and filter). It opens
   from an "Audit Log" button on Users (customer) and Users & Roles (platform).
9. **Platform Archive lists the customers whose plan includes the archive.** "Open Archive" opens the customer's
   workspace with a reason, like Customers > Open Workspace, and goes to its Archive screen.
10. **Platform Settings are used where a value exists today.** The offline-alert delay is the default for customers
    that never saved their own. The retention days drive the daily telemetry clean-up. The sender name and address
    are written into the e-mails of the development sender (there is no SMTP sender yet). The platform name is
    stored but not shown yet: the portal keeps its translated product name until the M10 review of the designs.
11. **One customer profile per customer.** `archive.CustomerProfiles` uses the tenant id as its key.

## Bugs fixed on the way

- After a reload, a platform user on any `/admin/...` page landed on the dashboard. The area guard ran before the
  session was restored. The area and permission guards now wait for the restore.
- Sign-in audit records named the actor "User", because nobody is signed in during sign-in. They now name the user
  and their customer.

## Consequences

The API surface is the one of 06, plus `/settings/integrations` (MC-904) and the two upload codes. The location
Archive tab is narrower than the customer Archive. The product owner may want a different location view.
