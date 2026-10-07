using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MonitorCloud.Application;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Infrastructure.Audit;
using MonitorCloud.Infrastructure.Licensing;
using MonitorCloud.Infrastructure.Messaging;
using MonitorCloud.Infrastructure.Options;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Monitor";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        services.AddSingleton(new DatabaseOptionsAccessor(connectionString));
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString, sql =>
        {
            sql.MigrationsHistoryTable("__EFMigrationsHistory", Schemas.Messaging);
            sql.EnableRetryOnFailure(3);
        }));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IReadDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<TenantScope>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantScope>());
        services.AddScoped<ITenantScopeSetter>(sp => sp.GetRequiredService<TenantScope>());

        services.AddScoped<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddLicensing(configuration);

        services.AddSingleton<Application.Identity.IPasswordHasher, Identity.PasswordHasherAdapter>();
        services.AddSingleton<Application.Identity.ITokenService, Identity.TokenService>();
        services.TryAddSingleton<Application.Abstractions.Email.IEmailSender, Email.DevelopmentEmailSender>();
        services.AddOptions<Email.PortalOptions>().Bind(configuration.GetSection(Email.PortalOptions.Section));
        services.AddSingleton<Application.Abstractions.Email.IPortalLinks, Email.PortalLinks>();
        services.AddScoped<Application.Tenancy.Contracts.ILocationCodeLookup, Tenancy.LocationCodeLookup>();
        services.AddOptions<Application.Devices.Contracts.AgentSettings>().Bind(configuration.GetSection(Application.Devices.Contracts.AgentSettings.Section));
        services.AddSingleton<Application.Devices.IDeviceSecretService, Devices.DeviceSecretService>();
        services.AddSingleton<Application.Devices.IDeviceTokenService, Devices.DeviceTokenService>();
        services.AddScoped<Application.Licensing.Contracts.IEnrollmentAttemptLog, Licensing.EnrollmentAttemptLog>();
        services.AddHostedService<Devices.DeviceMaintenanceJob>();
        services.AddScoped<Seeding.BootstrapSeeder>();
        services.AddScoped<Seeding.DemoSeeder>();

        services.AddSingleton(new EventTypeRegistry([typeof(Domain.AssemblyMarker).Assembly, typeof(DependencyInjection).Assembly, typeof(Application.DependencyInjection).Assembly]));
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.Section));
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());

        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.Section));
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.Section))
            .ValidateDataAnnotations()
            .Validate(o => Encoding.UTF8.GetByteCount(o.SigningKey) >= JwtOptions.MinimumKeyBytes,
                $"Jwt:SigningKey must be at least {JwtOptions.MinimumKeyBytes} bytes.")
            .Validate(o => Encoding.UTF8.GetByteCount(o.DeviceSigningKey) >= JwtOptions.MinimumKeyBytes,
                $"Jwt:DeviceSigningKey must be at least {JwtOptions.MinimumKeyBytes} bytes.")
            .Validate(o => !string.Equals(o.SigningKey, o.DeviceSigningKey, StringComparison.Ordinal),
                "Jwt:DeviceSigningKey must differ from Jwt:SigningKey.")
            .ValidateOnStart();

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database", tags: ["ready"])
            .AddCheck<OutboxLagHealthCheck>("outbox", tags: ["ready"])
            .AddCheck<LicensingHealthCheck>("licensing", tags: ["ready"]);

        return services;
    }

    /// <summary>Applies pending migrations when <c>Seed:ApplyMigrations</c> is true (Development and tests).</summary>
    public static async Task ApplyMigrationsAsync(this IServiceProvider services, bool force = false, CancellationToken cancellationToken = default)
    {
        var seed = services.GetRequiredService<IOptions<SeedOptions>>().Value;
        if (!seed.ApplyMigrations && !force)
            return;

        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>().RunAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}

public static class LicensingStartup
{
    /// <summary>Loads <c>Licensing:FakeDataPath</c> into the fake Licensing Platform (Fake mode only).</summary>
    public static void LoadFakeLicensingData(this IServiceProvider services, string contentRoot) =>
        Licensing.LicensingRegistration.LoadFakeData(services, contentRoot);
}
