using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Devices.Agent;

namespace MonitorCloud.AgentGateway.Sessions;

/// <summary>
/// Every 10 s (05 section 2, presence): closes sessions silent for 3 heartbeats, marks devices of closed streams
/// Offline after the 15 s grace period, and writes the last contact of the connected devices.
/// </summary>
public sealed partial class PresenceMonitor(
    IServiceScopeFactory scopes, IAgentSessionRegistry registry, IOptions<AgentGatewayOptions> options, TimeProvider clock, ILogger<PresenceMonitor> logger)
    : BackgroundService
{
    public const string TimeoutReason = "Timeout";
    public const string StreamClosedReason = "StreamClosed";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.PresenceEnabled)
            return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.PresenceIntervalSeconds)), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
    }

    /// <summary>One presence cycle; public for tests.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var offline = new List<OfflineDevice>();

        var silentBefore = now.AddSeconds(-settings.HeartbeatSeconds * settings.MissedHeartbeats);
        foreach (var session in registry.All.Where(s => s.LastMessageAt < silentBefore))
        {
            if (registry.Remove(session))
            {
                await session.DisconnectAsync("HEARTBEAT_TIMEOUT", "No message for three heartbeat intervals.", 5, now);
                offline.Add(new OfflineDevice(session.DeviceId, session.TenantId, TimeoutReason));
            }
        }

        offline.AddRange(registry.TakeExpiredClosures(now.AddSeconds(-settings.OfflineGraceSeconds))
            .Select(c => new OfflineDevice(c.DeviceId, c.TenantId, StreamClosedReason)));

        await using var scope = scopes.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        if (offline.Count > 0)
            await sender.Send(new MarkDevicesOfflineCommand(offline), cancellationToken);
        var connected = registry.All.Select(s => s.DeviceId).ToList();
        if (connected.Count > 0)
            await sender.Send(new TouchDevicesCommand(connected), cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Presence cycle failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>On shutdown every agent is told to come back in 5-30 s (05 section 2, rule 11).</summary>
public sealed class GatewayShutdown(IAgentSessionRegistry registry, TimeProvider clock) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await Task.WhenAll(registry.All.Select(s => s.DisconnectAsync(AgentSessionHandler.ServerShutdown, "The server is restarting.", (uint)Random.Shared.Next(5, 31), now)));
    }
}
