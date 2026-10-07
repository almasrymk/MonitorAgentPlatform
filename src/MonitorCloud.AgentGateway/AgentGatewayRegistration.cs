using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MonitorCloud.AgentGateway.Sessions;

namespace MonitorCloud.AgentGateway;

public static class AgentGatewayRegistration
{
    /// <summary>The authorization policy for device tokens (defined by the API host).</summary>
    public const string DevicePolicy = "Device";

    public static IServiceCollection AddAgentGateway(this IServiceCollection services, IConfiguration configuration)
    {
        // No catch-all route for unknown gRPC services: REST keeps its 404/405 problem answers.
        services.AddGrpc(o =>
        {
            o.MaxReceiveMessageSize = 8 * 1024 * 1024;
            o.IgnoreUnknownServices = true;
        });
        services.AddOptions<AgentGatewayOptions>().Bind(configuration.GetSection(AgentGatewayOptions.Section));
        services.AddSingleton<IAgentSessionRegistry, AgentSessionRegistry>();
        services.AddSingleton<AgentMessageRouter>();
        services.AddSingleton<AgentSessionHandler>();
        services.AddSingleton<PresenceMonitor>();
        services.AddHostedService(sp => sp.GetRequiredService<PresenceMonitor>());
        services.AddHostedService<GatewayShutdown>();
        services.AddScoped<Application.Abstractions.Messaging.IIntegrationEventHandler<Domain.Devices.DeviceRetiredV1>, CloseRetiredSessions>();
        services.AddHealthChecks().AddCheck<GatewayHealthCheck>("gateway", tags: ["ready"]);
        return services;
    }

    public static IEndpointRouteBuilder MapAgentGateway(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGrpcService<AgentGatewayService>();
        return endpoints;
    }
}

/// <summary>Reports the number of connected devices.</summary>
internal sealed class GatewayHealthCheck(IAgentSessionRegistry registry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(HealthCheckResult.Healthy($"{registry.All.Count} connected devices", new Dictionary<string, object> { ["sessions"] = registry.All.Count }));
}
