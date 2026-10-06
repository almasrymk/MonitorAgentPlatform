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
        services.TryAddScoped<ILocationDeviceCounter, NoDevicesCounter>();

        // Integration event handlers: every IIntegrationEventHandler<T> implementation in this assembly.
        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var handler in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>)))
                services.AddScoped(handler, type);
        }

        return services;
    }
}
