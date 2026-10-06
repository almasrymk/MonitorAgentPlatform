# Monitor Cloud

The multi-tenant platform behind the Monitor Agent product ("Monitor Agent Platform"): a .NET 10 API with an
agent gateway, an Angular portal and SQL Server. The specification is in [`docs/plan`](docs/plan/00-README.md);
working rules for contributors are in [`CLAUDE.md`](CLAUDE.md); progress is in [`docs/PROGRESS.md`](docs/PROGRESS.md).

## Requirements

- .NET SDK 10.0.x
- Node.js 22 and npm 10+
- SQL Server 2022 (local instance or a container)
- Docker is optional: without it, tests use a local SQL Server through `MONITOR_TEST_SQLSERVER`

## Run

```bash
dotnet tool restore
dotnet run --project src/MonitorCloud.Api --launch-profile http   # http://localhost:5300
cd portal && npm ci && npm start                                    # http://localhost:4300
```

Development settings (`src/MonitorCloud.Api/appsettings.Development.json`) use the default local SQL Server
instance with Windows authentication (`Server=.`), create the `MonitorCloud` database and apply migrations at
start-up. Secrets there are marked `DEV-ONLY-`; production values come from environment variables
(see [`docs/operations.md`](docs/operations.md)).

Useful endpoints: `/health/live`, `/health/ready`, `/openapi/v1.json`.

## Build and test

```bash
dotnet build MonitorCloud.slnx -c Release -warnaserror

# Integration tests need SQL Server: either Docker (Testcontainers) or a local instance:
export MONITOR_TEST_SQLSERVER="Server=.;Trusted_Connection=True;TrustServerCertificate=True"
dotnet test MonitorCloud.slnx -c Release

cd portal
npm run lint
npm test -- --watch=false
npx playwright install chromium   # once
npm run e2e
```

Test databases are named `MonitorCloud_Test_{guid}` and dropped at the end of a run.

## Migrations

```bash
dotnet ef migrations add <Name> -p src/MonitorCloud.Infrastructure -s src/MonitorCloud.Api -o Persistence/Migrations
```

## Layout

```
src/        SharedKernel, Domain, Application, Infrastructure, AgentProtocol, AgentGateway, Api
tests/      UnitTests, ArchitectureTests, IntegrationTests, GatewayTests, ContractTests, Shared
tools/      DeviceSimulator, LicensingSeeder
portal/     Angular application
proto/      device protocol (monitor.agent.v1)
docs/       plan, design, adr, operations, PROGRESS
```
