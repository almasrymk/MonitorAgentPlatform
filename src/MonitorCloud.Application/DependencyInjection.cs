using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Behaviors;
using MonitorCloud.Application.Identity;
using MonitorCloud.Application.Identity.Users;
using MonitorCloud.Application.Tenancy;
using MonitorCloud.Application.Tenancy.Contracts;

namespace MonitorCloud.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);

            // Order matters (01 section 3).
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(AuthorizationBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(EntitlementBehavior<,>));
            cfg.AddOpenBehavior(typeof(UnitOfWorkBehavior<,>));
            cfg.AddOpenBehavior(typeof(QueryCachingBehavior<,>));
        });
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddMemoryCache();

        services.AddScoped<AuthSessions>();
        services.AddScoped<UserRules>();
        services.AddScoped<TenantDirectory>();
        services.AddScoped<ITenantDirectory>(sp => sp.GetRequiredService<TenantDirectory>());
        services.AddScoped<ILocationDirectory>(sp => sp.GetRequiredService<TenantDirectory>());
        services.AddScoped<ILocationLookup, LocationLookup>();
        services.AddScoped<IEnrollmentCodeRedeemer, EnrollmentCodeRedeemer>();
        services.AddScoped<ITenantNames, TenantNames>();
        services.AddScoped<ILinkedTenantDirectory, LinkedTenantDirectory>();
        services.AddScoped<ITenantProvisioning, TenantProvisioning>();

        services.AddScoped<Licensing.LicensingSyncService>();
        services.AddScoped<Licensing.Contracts.IEntitlementRefresher>(sp => sp.GetRequiredService<Licensing.LicensingSyncService>());
        services.AddScoped<Abstractions.Entitlements.IEntitlementReader, Licensing.EntitlementReader>();
        services.AddScoped<Licensing.Contracts.IEntitlementDirectory, Licensing.EntitlementDirectory>();
        services.AddSingleton<Licensing.Contracts.ILicensingPolicy, Licensing.LicensingPolicy>();
        services.AddScoped<Licensing.Contracts.IDeviceSeats, Licensing.DeviceSeats>();
        services.AddScoped<Licensing.Contracts.IEntitlementStatistics, Licensing.EntitlementStatistics>();
        services.AddScoped<Licensing.DeviceLicenseRefreshService>();

        services.AddScoped<Devices.DeviceStatsDirectory>();
        services.AddScoped<Devices.Contracts.IDeviceStatsDirectory>(sp => sp.GetRequiredService<Devices.DeviceStatsDirectory>());
        services.AddScoped<ILocationDeviceCounter>(sp => sp.GetRequiredService<Devices.DeviceStatsDirectory>());
        services.AddScoped<Licensing.Contracts.IDeviceLicenseStats, Devices.DeviceLicenseStats>();
        services.AddScoped<Devices.DeviceGraceService>();
        services.TryAddSingleton<Abstractions.Realtime.ILiveNotifier, Abstractions.Realtime.NoLiveNotifier>();
        services.TryAddSingleton<Devices.Contracts.ILiveModeControl, Devices.Contracts.NoLiveModeControl>();
        services.AddScoped<Devices.Contracts.IDeviceDirectory, Devices.DeviceDirectory>();
        services.AddScoped<Devices.Contracts.IDeviceDashboardReader, Devices.DeviceDashboardReader>();
        services.AddScoped<Devices.Contracts.IDeviceNames, Devices.DeviceNames>();

        services.AddScoped<Monitoring.AlertBook>();
        services.AddScoped<Monitoring.Contracts.IOpenAlertCounter, Monitoring.OpenAlertCounter>();
        services.AddScoped<Monitoring.Contracts.IAlertDashboardReader, Monitoring.AlertDashboardReader>();
        services.AddScoped<Monitoring.Contracts.IMonitoringSettingsStore, Monitoring.MonitoringSettingsStore>();
        services.AddScoped<Monitoring.OfflineAlertService>();

        services.AddScoped<Monitoring.Contracts.IMonitorPointCatalog, Monitoring.MonitorPointCatalog>();
        services.AddScoped<Configuration.ConfigurationService>();
        services.AddScoped<Configuration.Contracts.IConfigurationVersioning>(sp => sp.GetRequiredService<Configuration.ConfigurationService>());
        services.AddScoped<Configuration.Contracts.IAgentConfigurationReader, Configuration.AgentConfigurationReader>();

        services.AddScoped<Devices.Contracts.IDeviceReportReader, Devices.DeviceReportReader>();
        services.AddScoped<Telemetry.Contracts.ITelemetryReportReader, Telemetry.TelemetryReportReader>();
        services.AddScoped<Monitoring.Contracts.IAlertReportReader, Monitoring.AlertReportReader>();
        services.AddScoped<Media.Contracts.IMediaFiles, Media.MediaFiles>();
        services.AddScoped<Reports.ReportEngine>();
        services.AddScoped<Reports.ReportGenerationService>();
        services.AddScoped<ITenantFacts, TenantFacts>();
        services.AddScoped<IPlatformDefaults, PlatformDefaults>();

        services.AddScoped<Notifications.NotificationFanOut>();
        services.AddScoped<Notifications.Contracts.INotificationPublisher, Notifications.NotificationPublisher>();
        services.AddScoped<Notifications.NotificationDeliveryService>();

        // Integration event handlers: every IIntegrationEventHandler<T> implementation in this assembly.
        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var handler in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>)))
                services.AddScoped(handler, type);
        }

        return services;
    }
}
