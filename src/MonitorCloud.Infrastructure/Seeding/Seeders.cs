using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Tenancy;
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
/// Part 1 (M1): platform users, the 48 customers, their locations and users.
/// </summary>
public sealed partial class DemoSeeder(
    AppDbContext db,
    Application.Identity.IPasswordHasher hasher,
    IHostEnvironment environment,
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
        var now = clock.GetUtcNow();
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        string Hash(string password) => hashes.TryGetValue(password, out var h) ? h : hashes[password] = hasher.Hash(password);

        foreach (var (email, password, role, name) in DemoData.PlatformUsers)
        {
            if (!await db.Set<User>().AnyAsync(u => u.Email == email, cancellationToken))
                db.Set<User>().Add(User.CreatePlatformUser(email, name, role, Hash(password), now));
        }

        foreach (var spec in DemoData.Detailed)
            AddCustomer(spec, now, Hash, detailedUsers: true);

        var plans = new[] { "ENTERPRISE", "BUSINESS", "PROFESSIONAL", "STARTER" };
        var suspendedIndex = random.Next(9, 49);
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
            var spec = new DemoData.CustomerSpec(
                $"Customer {number:00}", $"C{number:00}", plan, number == suspendedIndex, random.Next(5, 41), random.Next(10, 360),
                random.Next(2, 60), city.Country, city.City, city.TimeZone, locations);
            AddCustomer(spec, now, Hash, detailedUsers: false);
        }

        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, DemoData.Detailed.Length + 40);
    }

    private void AddCustomer(DemoData.CustomerSpec spec, DateTimeOffset now, Func<string, string> hash, bool detailedUsers)
    {
        var since = DateOnly.FromDateTime(now.UtcDateTime).AddMonths(-spec.SinceMonths);
        var tenant = Tenant.Create(spec.Name, spec.Code, spec.Country, spec.City, spec.TimeZone, since, null, now);
        tenant.ClearDomainEvents();
        db.Set<Tenant>().Add(tenant);

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

        if (spec.Suspended)
            tenant.Suspend("Subscription suspended", now);
        tenant.ClearDomainEvents();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo data seeded: {Customers} customers")]
    private static partial void LogSeeded(ILogger logger, int customers);
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
