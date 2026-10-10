# ADR 0010 - Hardening decisions (M10)

- Status: Proposed (applied in M10, awaiting product-owner review)
- Date: 2026-10-10

## Context

M10 hardens the release: security tests, performance, accessibility, Arabic/RTL and the production configuration.
A few findings needed changes the plan does not describe, and one target needed an interpretation.

## Decisions

1. **The API refuses to start in Production with unsafe settings.** It checks development or test keys, keys that
   are too short or equal, fake Licensing, demo seed, local or non-https CORS origins and public addresses, an
   empty `Storage:Root`, `AllowedHosts=*` and debug logging. It lists every problem by setting name and never prints
   a value (docs/operations.md section 1).
2. **Key rotation keeps the old keys for a while.** The signing keys also key the hashes of refresh, invitation and
   device secrets. Rotating one would have signed every user out and forced every agent to re-enroll. With
   `Jwt:PreviousSigningKeys` / `Jwt:PreviousDeviceSigningKeys`:
   - tokens signed with the old key stay valid;
   - secrets hashed with it are still found;
   - device secrets are re-hashed with the new key at the next token exchange.

   The procedure is in docs/operations.md section 5.
3. **Forwarded headers come only from configured proxies.** With `ReverseProxy:KnownProxies` / `KnownNetworks`, the
   client address and scheme come from `X-Forwarded-For` / `X-Forwarded-Proto`. Without this, behind a proxy every
   client shared one rate-limit bucket and HTTPS was not detected. Other senders cannot spoof the header.
4. **Token refresh has its own rate limit.** Sign-in and invitations: 20 per minute and IP (password spraying).
   Refresh and sign-out: 300 per minute and IP. Every open portal refreshes every 15 minutes, and a whole office
   often shares one address, so with the old shared limit of 20 a 300-person office would be refused.
5. **Colour tokens changed for WCAG AA (4.5:1).** axe found serious contrast failures:
   - Dark theme (the theme of the mockups): only `--mc-text-faint`, `#475263` to `#7385A0` (2.3 to 4.6:1). It is
     used for captions, the version line and labels.
   - Light theme: its values are marked as estimates in `tokens.scss`. Muted, faint, brand/success, warning and
     danger are darkened (e.g. brand `#17A34A` to `#127F3A` as text on white, 3.3 to 4.7:1).
   - New tokens `--mc-danger-solid` and `--mc-on-solid` give white text on solid red (the bell badge was 3.5:1).
   - The neutral status pill uses the secondary text colour for its text; its dot keeps the neutral colour.

   The change is the same in `docs/design/tokens.scss` and the portal copy.
6. **The Lighthouse target is met on the desktop profile.** Lighthouse runs on the signed-in Customer Dashboard of
   the production build: desktop 91-93, mobile profile 62-67. Every design is a desktop console. The mobile profile
   is bound by the round trips before the first paint (script, session restore, route, data). Fixing that needs
   server-side rendering, which is not planned. If mobile matters, a pre-rendered shell is the next step.
7. **Server-generated report texts stay English in CSV and PDF files.** The portal translates report column and
   summary labels by key on screen. The downloaded files carry the server's English labels. Localised files would
   need the requester's language in the report job. Left for a later release.

## Bugs found and fixed in M10

- Arabic screens with dates (Customer Dashboard, alerts, device About) threw `Missing locale data for "ar-EG"`, so
  parts of those screens never rendered. The Arabic locale data is now registered at start-up.
- ECharts was loaded as two full barrels (2 x 262 kB) instead of the five parts used.
- Remaining accessibility issues: progress bars had no accessible name; the unread dot had an `aria-label` without
  a role; the Report Type list used listbox roles on buttons; the toast close button was labelled in English only.

## Consequences

Operators must set `ReverseProxy:*` behind a proxy and keep the old keys during a rotation. The colour changes are
small and visible mostly in the light theme.
