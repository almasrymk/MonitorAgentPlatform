# ADR 0011 - Remote actions details (M11)

- Status: Proposed (applied in M11, awaiting product-owner review)
- Date: 2026-10-10

## Context

05 section 9 fixes the rules of remote actions: six types, ES256 signature with `Commands:SigningKey`, the public
key received at enrollment, expiry of at most 5 minutes, a nonce kept 10 minutes, the device id inside the signature,
the plan feature, `devices.manage`, `features.remoteActions` and the local switch `Cloud:AllowRemoteActions`. It
does not fix the byte format of the signed text, how keys are named, what happens to a command nobody answers,
where the history is shown or how the device setting is changed in the portal.

## Decisions

1. **Signed text.** UTF-8 of `command_id|type|parameters_json|expires_at|nonce|device_id`: both ids as 32 lower-case
   hex digits (`Guid.ToString("N")`), `expires_at` as Unix milliseconds (the stored expiry is cut to whole
   milliseconds so both sides compute the same number), the nonce as 32 hex digits (128 random bits). The signature
   is ES256 in IEEE P1363 form (64 bytes: r then s), the form JOSE uses.
2. **Keys.** `Commands:SigningKey` holds the P-256 private key as PKCS#8 PEM. The `kid` is the RFC 7638 thumbprint
   of the public key. Enrollment returns `commandSigningKeys` as a JWK set with the current key and the public keys
   in `Commands:PreviousPublicKeys` (rotation, docs/operations.md section 5). Outside Production an empty value
   creates a key once in `{Storage:Root}/keys/command-signing.pem` so enrolled development agents keep verifying
   after a restart; Production refuses to start without the key (MC-1006 check).
3. **Lifecycle.** `Pending` (stored, not yet on a stream) -> `Sent` -> `Succeeded` / `Failed` / `Rejected`
   (refused by the agent) / `Expired`. A connected device gets the command at once; an offline device gets it right
   after its next `Welcome` while it has not expired. Every 30 s a job marks open commands `Expired` one minute
   after their expiry (the grace minute covers a result still in flight). A late answer after that is still
   recorded; a second answer is refused with `COMMAND_COMPLETED`. Requests, results and expiries are audited
   (`device.command.requested`, `device.command.completed`, `device.command.expired`; the reason is in the details).
4. **Device setting in the portal.** The device Settings tab and Settings > Monitoring got an "Allow remote actions"
   check box for `features.remoteActions` (it is part of the configuration document of 05 section 8, which had no
   field for it on screen). The demo seed turns it on for the fixed WEB-SRV-01 of Cairo HQ only.
5. **Where the history is.** The Remote Actions button on the device header (07 section 5.7) opens a menu with the
   six actions and "Command history"; the history opens in the side drawer. The button is absent unless
   `GET /devices/{id}/remote-actions` says available (feature, licence, device setting) and the user has
   `devices.manage`.
6. **Agent side (AG-13, branch `cloud/m11-commands` of UBGMonitor).** Same checks in the same order as the
   simulator. Seen nonces are kept in `cloud.db` so a restart does not reopen the replay window. Service names must
   match `^[A-Za-z0-9_.@-]{1,128}$` and are passed as one argument to `ServiceController`, `systemctl` or
   `launchctl`, never through a shell. The agent's own service is changed only by `restart-agent`, which exits with
   code 1 after 5 s so the service manager restarts it. Agents enrolled before M11 have no keys and refuse every
   command ("Unknown signing key") until they enroll again.

## Consequences

- The cloud and the agent share one canonical text; a test on each side signs and verifies it.
- Rotating the command key needs the previous public key published until every agent re-enrolled; enrollment is the
  only time agents receive keys (05 section 1).
