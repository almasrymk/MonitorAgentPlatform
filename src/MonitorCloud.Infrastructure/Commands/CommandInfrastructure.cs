using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Commands;
using MonitorCloud.Domain.Commands;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.Infrastructure.Commands;

internal sealed class DeviceCommandConfiguration : IEntityTypeConfiguration<DeviceCommand>
{
    public void Configure(EntityTypeBuilder<DeviceCommand> builder)
    {
        builder.ToTable("DeviceCommands", Schemas.Commands);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Type).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ParametersJson).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.RequestedByName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Nonce).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.Output).HasColumnType("nvarchar(max)");
        builder.HasIndex(x => new { x.DeviceId, x.RequestedAt }).IsDescending(false, true);
        builder.HasIndex(x => new { x.Status, x.ExpiresAt });
        builder.HasIndex(x => x.TenantId);
    }
}

/// <summary>Closes unanswered commands after their expiry every 30 s (off with the background jobs in tests, which call <see cref="RunOnceAsync"/>).</summary>
public sealed partial class CommandsJob(IServiceScopeFactory scopes, IOptions<CommandOptions> options, IOptions<Application.Devices.Contracts.AgentSettings> agent, TimeProvider clock, ILogger<CommandsJob> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.JobsEnabled || !agent.Value.BackgroundJobsEnabled)
            return;
        using var timer = new PeriodicTimer(Interval, clock);
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

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>().RunAsSystem();
        return await scope.ServiceProvider.GetRequiredService<CommandExpiryService>().RunAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Command expiry failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
