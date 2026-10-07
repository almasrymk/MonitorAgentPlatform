using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Devices;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Licensing;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Devices;

/// <summary>
/// Device maintenance in the system scope: refreshes due device licences every five minutes (04 section 4.2), ends
/// the grace period of unlicensed devices (D19) and removes enrollment attempts older than 90 days (02 section 3).
/// </summary>
public sealed partial class DeviceMaintenanceJob(IServiceScopeFactory scopes, IOptions<AgentSettings> options, TimeProvider clock, ILogger<DeviceMaintenanceJob> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan AttemptRetention = TimeSpan.FromDays(90);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.BackgroundJobsEnabled)
            return;

        using var timer = new PeriodicTimer(Interval, clock);
        do
        {
            try
            {
                await RunOnceAsync(scopes, clock, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One cycle; public for tests and the CLI.</summary>
    public static async Task RunOnceAsync(IServiceScopeFactory scopes, TimeProvider clock, CancellationToken ct)
    {
        await using (var scope = scopes.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>().RunAsSystem();
            await scope.ServiceProvider.GetRequiredService<DeviceLicenseRefreshService>().RunAsync(ct);
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>().RunAsSystem();
            await scope.ServiceProvider.GetRequiredService<DeviceGraceService>().RunAsync(ct);
        }

        await using (var scope = scopes.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>().RunAsSystem();
            var cutoff = clock.GetUtcNow().Subtract(AttemptRetention);
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<EnrollmentAttempt>().Where(a => a.At < cutoff).ExecuteDeleteAsync(ct);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Device maintenance cycle failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
