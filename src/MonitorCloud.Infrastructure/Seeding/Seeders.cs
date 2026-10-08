using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.Application.Licensing;
using MonitorCloud.Infrastructure.Licensing.Fake;
using MonitorCloud.Infrastructure.Options;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Seeding;

/// <summary>Creates the first Platform Admin when <c>Seed:AdminEmail</c>/<c>Seed:AdminPassword</c> are set and none exists (08).</summary>
public sealed partial class BootstrapSeeder(AppDbContext db, Application.Identity.IPasswordHasher hasher, IOptions<SeedOptions> options, TimeProvider clock, ILogger<BootstrapSeeder> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var seed = options.Value;
        if (string.IsNullOrWhiteSpace(seed.AdminEmail) || string.IsNullOrWhiteSpace(seed.AdminPassword))
            return;
        if (await db.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.TenantId == null, cancellationToken))
            return;
        if (PasswordPolicy.Check(seed.AdminPassword, seed.AdminEmail) is { } weak)
            throw new InvalidOperationException($"Seed:AdminPassword is too weak: {weak}");

        db.Set<User>().Add(User.CreatePlatformUser(seed.AdminEmail, "Platform Administrator", Roles.PlatformAdmin, hasher.Hash(seed.AdminPassword), clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        LogCreated(logger, User.NormalizeEmail(seed.AdminEmail));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Bootstrap Platform Admin {Email} created")]
    private static partial void LogCreated(ILogger logger, string email);
}

/// <summary>
/// The demo data of 08, deterministic (<c>new Random(20261006)</c>, dates relative to the moment of seeding).
/// Part 1 (M1): platform users, the 48 customers, their locations and users. Part 2 (M3): devices, device state,
/// licence rows and the inventory of the fixed devices.
/// </summary>
public sealed partial class DemoSeeder(
    AppDbContext db,
    Application.Identity.IPasswordHasher hasher,
    IHostEnvironment environment,
    FakeLicensingStore licensingStore,
    LicensingSyncService licensingSync,
    IOptions<LicensingSettings> licensingSettings,
    TimeProvider clock,
    ILogger<DemoSeeder> logger)
{
    public async Task RunAsync(bool reset, CancellationToken cancellationToken)
    {
        if (environment.IsProduction())
            throw new InvalidOperationException("The demo seed refuses to run in Production.");

        if (reset)
            await DatabaseReset.DeleteAllAsync(db, cancellationToken);

        if (await db.Set<Tenant>().AnyAsync(t => t.Code == "ACME", cancellationToken))
            return;

        var random = new Random(DemoData.Seed);
        var licensingRandom = new Random(DemoData.Seed + 1);
        var now = clock.GetUtcNow();
        var licensing = DemoLicensing.NewData(licensingRandom);
        var devices = new DemoDevices(db, new Random(DemoData.Seed + 2), now, TimeSpan.FromDays(licensingSettings.Value.UnlicensedGraceDays),
            licensingSettings.Value.IsLive ? null : licensingStore);
        var customers = new Dictionary<Guid, (string Name, string Code)>();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        string Hash(string password) => hashes.TryGetValue(password, out var h) ? h : hashes[password] = hasher.Hash(password);

        foreach (var (email, password, role, name) in DemoData.PlatformUsers)
        {
            if (!await db.Set<User>().AnyAsync(u => u.Email == email, cancellationToken))
                db.Set<User>().Add(User.CreatePlatformUser(email, name, role, Hash(password), now));
        }

        foreach (var spec in DemoData.Detailed)
        {
            // About 5% of a detailed customer's devices are unlicensed; Acme follows 08 exactly (26 of 316).
            var unlicensed = spec.Code == "ACME" ? 26 : (int)Math.Round(spec.Devices * 0.05);
            AddCustomer(spec, spec.Devices - unlicensed, now, Hash, detailedUsers: true, licensing, licensingRandom, customers, devices, Array.IndexOf(DemoData.Detailed, spec) + 1);
        }

        var plans = new[] { "ENTERPRISE", "BUSINESS", "PROFESSIONAL", "STARTER" };
        var suspendedIndex = random.Next(9, 49);
        // Four generated customers renew within 30 days (08 section 3): six "expiring soon" with Horizon and Gulf.
        var expiring = Enumerable.Range(9, 40).Where(n => n != suspendedIndex).OrderBy(_ => licensingRandom.Next()).Take(4).ToHashSet();
        for (var number = 9; number <= 48; number++)
        {
            var roll = random.NextDouble();
            var plan = roll < 0.30 ? plans[0] : roll < 0.60 ? plans[1] : roll < 0.85 ? plans[2] : plans[3];
            var city = DemoData.Cities[random.Next(DemoData.Cities.Length)];
            var locationCount = random.Next(1, 3);
            var locations = Enumerable.Range(1, locationCount)
                .Select(i => DemoData.Cities[(Array.IndexOf(DemoData.Cities, city) + i - 1) % DemoData.Cities.Length])
                .Select((c, i) => new DemoData.LocationSpec(i == 0 ? $"{c.City} Office" : $"{c.City} Branch", $"L{i + 1}", c.City, c.Country, c.TimeZone))
                .ToArray();
            var deviceCount = random.Next(5, 41);
            _ = random.Next(10, 360);
            var renewsIn = expiring.Contains(number) ? licensingRandom.Next(5, 30) : licensingRandom.Next(45, 360);
            var spec = new DemoData.CustomerSpec(
                $"Customer {number:00}", $"C{number:00}", plan, number == suspendedIndex, deviceCount, renewsIn,
                random.Next(2, 60), city.Country, city.City, city.TimeZone, locations);
            AddCustomer(spec, deviceCount - licensingRandom.Next(0, 3), now, Hash, detailedUsers: false, licensing, licensingRandom, customers, devices, number);
        }

        await db.SaveChangesAsync(cancellationToken);

        if (!licensingSettings.Value.IsLive)
        {
            licensingStore.Load(licensing);
            if (!string.IsNullOrWhiteSpace(licensingSettings.Value.FakeDataPath))
            {
                var path = Path.GetFullPath(Path.Combine(environment.ContentRootPath, licensingSettings.Value.FakeDataPath));
                FakeLicensingStore.Save(licensing, path);
                await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(path)!, "README.md"), DemoLicensing.Readme(licensing, customers), cancellationToken);
            }

            // Entitlements come from the (fake) Licensing Platform, exactly as in production.
            await licensingSync.ReconcileAllAsync(cancellationToken);
        }

        // Part 3 (M5): metric history, disk usage and snapshots (bulk copy, after the devices exist).
        var telemetry = new DemoTelemetry(db.Database.GetConnectionString()!, now, new Random(DemoData.Seed + 3));
        await telemetry.RunAsync(cancellationToken);

        // Part 4 (M6): alerts, daily statistics, notifications, audit activity, monitor points, recipients.
        var monitoring = new DemoMonitoring(db, now, new Random(DemoData.Seed + 4));
        await monitoring.RunAsync([.. DemoData.Detailed.Select(d => d.Code)], cancellationToken);

        // 08 section 4: one valid enrollment code for Acme / Cairo HQ, printed at seed time.
        var cairo = await db.Set<Location>().FirstAsync(l => l.Code == "CAIRO-HQ", cancellationToken);
        var code = Application.Tenancy.EnrollmentCodes.New();
        db.Set<LocationEnrollmentCode>().Add(LocationEnrollmentCode.Create(cairo.TenantId, cairo.Id, Application.Tenancy.EnrollmentCodes.Hash(code),
            Application.Tenancy.EnrollmentCodes.Prefix(code), TimeSpan.FromDays(30), null, null, now));
        await db.SaveChangesAsync(cancellationToken);
        LogEnrollmentCode(logger, code);

        LogSeeded(logger, DemoData.Detailed.Length + 40, devices.Created);
        LogTelemetry(logger, telemetry.Rows);
    }

    private void AddCustomer(
        DemoData.CustomerSpec spec, int licensedDevices, DateTimeOffset now, Func<string, string> hash, bool detailedUsers,
        FakeLicensingData licensing, Random licensingRandom, Dictionary<Guid, (string Name, string Code)> customers, DemoDevices devices, int tenantNumber)
    {
        var since = DateOnly.FromDateTime(now.UtcDateTime).AddMonths(-spec.SinceMonths);
        var customerId = DemoLicensing.NewGuid(licensingRandom);
        var tenant = Tenant.Create(spec.Name, spec.Code, spec.Country, spec.City, spec.TimeZone, since, customerId, now);
        tenant.ClearDomainEvents();
        db.Set<Tenant>().Add(tenant);
        customers[customerId] = (spec.Name, spec.Code);
        var sinceAt = new DateTimeOffset(since.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var renews = spec.Suspended ? now.AddDays(60) : now.AddDays(spec.RenewsInDays);
        DemoLicensing.AddCustomer(licensing, licensingRandom, customerId, spec.Name, spec.Code, spec.Country, spec.Plan, spec.Suspended, sinceAt, renews, licensedDevices);

        db.Set<Location>().Add(Location.CreateDefault(tenant.Id, spec.TimeZone, now));
        var locations = spec.Locations
            .Select(l => Location.Create(tenant.Id, new LocationDetails(l.Name, l.Code, l.City, l.Country, null, l.TimeZone, null, null, null), now))
            .ToList();
        db.Set<Location>().AddRange(locations);

        var domain = $"{spec.Code.ToLowerInvariant()}.test";
        var demoHash = hash(DemoData.DemoPassword);
        void AddUser(string localPart, string name, string role, Guid[]? scope = null, bool inactive = false)
        {
            var user = User.CreateTenantUser(tenant.Id, $"{localPart}@{domain}", name, role, scope ?? [], demoHash, now);
            if (inactive)
                user.Deactivate(otherActiveAdministrators: 1);
            db.Set<User>().Add(user);
        }

        AddUser("admin", $"{spec.Name} Administrator", Roles.Administrator);
        if (detailedUsers)
        {
            AddUser("it", "IT Manager", Roles.ITManager);
            AddUser("tech", "Technician", Roles.Technician);
            AddUser("viewer", "Report Viewer", Roles.ReportViewer);
            if (spec.Code == "ACME")
            {
                AddUser("admin2", "Second Administrator", Roles.Administrator);
                AddUser("it.cairo", "Cairo IT Manager", Roles.ITManager, [locations[0].Id]);
                AddUser("it2", "Second IT Manager", Roles.ITManager);
                AddUser("tech.inactive", "Former Technician", Roles.Technician, inactive: true);
            }
        }

        var license = licensing.Licenses[^1];
        var plans = spec.Code == "ACME"
            ? DemoDevices.Acme
            : devices.GenericPlan(spec.Devices, spec.Devices - licensedDevices, locations.Count);
        devices.AddCustomer(tenant, spec.Code, locations, plans, license, [.. DemoLicensing.Plans.First(p => p.Code == spec.Plan).Features], spec.Plan, tenantNumber);

        if (spec.Suspended)
            tenant.Suspend("Subscription suspended", now);
        tenant.ClearDomainEvents();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo enrollment code for Acme / Cairo HQ (valid 30 days): {Code}")]
    private static partial void LogEnrollmentCode(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo telemetry seeded: {Rows} metric rows")]
    private static partial void LogTelemetry(ILogger logger, int rows);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo data seeded: {Customers} customers, {Devices} devices")]
    private static partial void LogSeeded(ILogger logger, int customers, int devices);
}
/// <summary>Deletes every business row in dependency order (keeps the migrations history).</summary>
internal static class DatabaseReset
{
    public static async Task DeleteAllAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var tables = db.Model.GetEntityTypes()
            .Where(t => t.GetTableName() is not null)
            .Select(t => (Schema: t.GetSchema() ?? "dbo", Table: t.GetTableName()!, Type: t))
            .DistinctBy(t => (t.Schema, t.Table))
            .ToList();

        var ordered = new List<(string Schema, string Table)>();
        var visited = new HashSet<(string, string)>();
        void Visit((string Schema, string Table, Microsoft.EntityFrameworkCore.Metadata.IEntityType Type) table)
        {
            if (!visited.Add((table.Schema, table.Table)))
                return;
            foreach (var dependant in tables.Where(t => t.Type.GetForeignKeys().Any(fk => fk.PrincipalEntityType == table.Type) && t.Type != table.Type))
                Visit(dependant);
            ordered.Add((table.Schema, table.Table));
        }

        foreach (var table in tables)
            Visit(table);

        // Table names come from the EF model, never from input.
        var script = string.Join(Environment.NewLine, ordered.Select(t => $"DELETE FROM [{t.Schema}].[{t.Table}];"));
        await db.Database.ExecuteSqlRawAsync(script, cancellationToken);
    }
}

/// <summary>Runs the seeders at start-up (after the migrations).</summary>
public static class SeedRunner
{
    public static async Task SeedAsync(this IServiceProvider services, bool reset = false, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>().RunAsSystem();
        await scope.ServiceProvider.GetRequiredService<BootstrapSeeder>().RunAsync(cancellationToken);
        var options = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;
        if (options.DemoData || reset)
            await scope.ServiceProvider.GetRequiredService<DemoSeeder>().RunAsync(reset, cancellationToken);
    }
}
