using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Monitoring;
using MonitorCloud.Application.Notifications;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Monitoring;

/// <summary>
/// Every 30 s in the system scope: due <c>device-offline</c> alerts, pending e-mail deliveries and, once a minute, the
/// <c>MonitorPointSampler</c> (one <c>telemetry.MonitorPointSamples</c> row per enabled point and minute).
/// </summary>
public sealed partial class MonitoringJobs(IServiceScopeFactory scopes, DatabaseOptionsAccessor database, IOptions<AgentSettings> options, TimeProvider clock, ILogger<MonitoringJobs> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private DateTime _lastSample = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.BackgroundJobsEnabled)
            return;
        using var timer = new PeriodicTimer(Interval, clock);
        do
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
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One cycle; public for tests.</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>().RunAsSystem();
            await scope.ServiceProvider.GetRequiredService<OfflineAlertService>().RunAsync(ct);
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>().RunAsSystem();
            await scope.ServiceProvider.GetRequiredService<NotificationDeliveryService>().RunAsync(ct);
        }

        var minute = Minute(clock.GetUtcNow().UtcDateTime);
        if (minute > _lastSample)
        {
            await SampleMonitorPointsAsync(ct);
            _lastSample = minute;
        }
    }

    /// <summary>Writes the current status of every enabled monitor point for this minute (idempotent). Returns the rows added.</summary>
    public async Task<int> SampleMonitorPointsAsync(CancellationToken ct)
    {
        var bucket = Minute(clock.GetUtcNow().UtcDateTime);
        await using var connection = new SqlConnection(database.ConnectionString);
        return await connection.ExecuteAsync(new Dapper.CommandDefinition("""
            INSERT INTO telemetry.MonitorPointSamples (MonitorPointId, BucketUtc, TenantId, DeviceId, Status, ResponseMs)
            SELECT s.MonitorPointId, @bucket, s.TenantId, s.DeviceId, s.Status, s.ResponseMs
            FROM monitoring.MonitorPointStates s
            JOIN monitoring.MonitorPoints p ON p.Id = s.MonitorPointId
            WHERE p.Enabled = 1
              AND NOT EXISTS (SELECT 1 FROM telemetry.MonitorPointSamples x WHERE x.MonitorPointId = s.MonitorPointId AND x.BucketUtc = @bucket)
            """, new { bucket }, cancellationToken: ct));
    }

    private static DateTime Minute(DateTime utc) => new(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);

    [LoggerMessage(Level = LogLevel.Error, Message = "Monitoring job cycle failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
