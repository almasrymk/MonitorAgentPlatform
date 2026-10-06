# Operations

Skeleton created in M0 (MC-010); completed in M10 (MC-1005).

## 1. Configuration reference

| Key | Default | Notes |
|---|---|---|
| `ConnectionStrings:Monitor` | - | SQL Server connection string. Required. |
| `Jwt:SigningKey` | - | HS256 key for user tokens, at least 32 bytes. From the environment outside Development. The API refuses to start without it. |
| `Jwt:DeviceSigningKey` | - | HS256 key for device tokens, at least 32 bytes, different from `Jwt:SigningKey`. |
| `Jwt:Issuer` / `Jwt:Audience` / `Jwt:DeviceAudience` | `monitor-cloud` / `monitor-cloud-portal` / `monitor-agent-gateway` | |
| `Cors:Origins` | `[]` | Explicit portal origins. |
| `Seed:ApplyMigrations` | `false` (`true` in Development) | Apply EF Core migrations at start-up. |
| `Seed:DemoData` | `false` (`true` in Development) | Demo seed; refuses to run in Production. |
| `Outbox:PollInterval` / `Outbox:BatchSize` / `Outbox:Enabled` | `00:00:02` / `50` / `true` | Outbox dispatcher. |
| `Serilog:*` | console | Structured logging. |

Environment variables use `__` as the separator, e.g. `Jwt__SigningKey`.

## 2. Health

- `GET /health/live` - process is up (no dependency checks).
- `GET /health/ready` - database reachable, outbox lag under 5 minutes (degraded above), gateway (from M4).

## 3. Migrations as a release step

Outside Development, migrations are applied before the new version starts:

```bash
dotnet ef migrations script --idempotent -p src/MonitorCloud.Infrastructure -s src/MonitorCloud.Api -o migrate.sql
```

Run `migrate.sql` against the target database, then deploy. To be completed in M10.

## 4. Backup and restore

To be written in M10.

## 5. Key rotation (JWT, device, command-signing)

To be written in M10.

## 6. Runbooks

- Licensing Platform unavailable - M10.
- Gateway overloaded - M10.
- Database full - M10.
