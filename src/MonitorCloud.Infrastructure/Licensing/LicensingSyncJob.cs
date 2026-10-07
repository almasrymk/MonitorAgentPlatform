using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Licensing;

namespace MonitorCloud.Infrastructure.Licensing;

/// <summary>Runs <see cref="LicensingSyncService"/> every <c>SyncIntervalSeconds</c> in the system scope (04 section 4.3).</summary>
public sealed partial class LicensingSyncJob(IServiceScopeFactory scopes, IOptions<LicensingSettings> options, TimeProvider clock, ILogger<LicensingSyncJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.SyncEnabled)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.SyncIntervalSeconds)), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>().RunAsSystem();
                await scope.ServiceProvider.GetRequiredService<LicensingSyncService>().RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Licensing sync cycle failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
