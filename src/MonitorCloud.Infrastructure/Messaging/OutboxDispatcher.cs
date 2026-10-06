using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Infrastructure.Persistence;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Infrastructure.Messaging;

public sealed class OutboxOptions
{
    public const string Section = "Outbox";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
    public int BatchSize { get; set; } = 50;
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Polls <c>messaging.OutboxMessages</c> and dispatches each event to its <see cref="IIntegrationEventHandler{TEvent}"/>s.
/// Each handler is recorded in <c>messaging.InboxMessages</c> in the same save as its effects, so it runs once.
/// </summary>
public sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    EventTypeRegistry registry,
    TimeProvider timeProvider,
    Microsoft.Extensions.Options.IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return;

        using var timer = new PeriodicTimer(options.Value.PollInterval, timeProvider);
        do
        {
            try
            {
                while (await DispatchBatchAsync(stoppingToken) == options.Value.BatchSize)
                {
                    // A full batch: keep draining before waiting.
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogLoopFailed(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Dispatches one batch of due messages. Returns how many messages were taken.</summary>
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>().RunAsSystem();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = timeProvider.GetUtcNow();

        var batch = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && !m.DeadLettered && m.NextAttemptAt <= now)
            .OrderBy(m => m.OccurredAt)
            .Take(options.Value.BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var message in batch)
        {
            try
            {
                await DispatchAsync(message, cancellationToken);
                message.MarkProcessed(timeProvider.GetUtcNow());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.MarkFailed($"{ex.GetType().Name}: {ex.Message}", timeProvider.GetUtcNow());
                if (message.DeadLettered)
                    LogDeadLettered(logger, message.Id, message.Type, ex);
                else
                    LogRetry(logger, message.Id, message.Type, message.Attempts, ex);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return batch.Count;
    }

    private async Task DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var eventType = registry.Find(message.Type)
            ?? throw new InvalidOperationException($"Unknown event type '{message.Type}'.");
        var domainEvent = (IDomainEvent)(JsonSerializer.Deserialize(message.Payload, eventType, OutboxSerializer.Options)
            ?? throw new InvalidOperationException("Empty payload."));

        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
        await using (var probe = scopeFactory.CreateAsyncScope())
        {
            var count = probe.ServiceProvider.GetServices(handlerType).Count();
            for (var index = 0; index < count; index++)
                await RunHandlerAsync(message, domainEvent, handlerType, index, cancellationToken);
        }
    }

    private async Task RunHandlerAsync(OutboxMessage message, IDomainEvent domainEvent, Type handlerType, int index, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>();
        if (message.TenantId is { } tenantId)
            tenantScope.RunAsTenant(tenantId);
        else
            tenantScope.RunAsSystem();

        var handler = scope.ServiceProvider.GetServices(handlerType).ElementAt(index)!;
        var handlerName = handler.GetType().FullName!;
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.InboxMessages.AnyAsync(i => i.MessageId == message.Id && i.Handler == handlerName, cancellationToken))
            return;

        var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IDomainEvent>.HandleAsync))!;
        await (Task)method.Invoke(handler, BindingFlags.DoNotWrapExceptions, binder: null, [domainEvent, cancellationToken], culture: null)!;

        db.InboxMessages.Add(InboxMessage.Create(message.Id, handlerName, timeProvider.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox dispatch loop failed")]
    private static partial void LogLoopFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({Type}) failed, attempt {Attempts}; will retry")]
    private static partial void LogRetry(ILogger logger, Guid messageId, string type, int attempts, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} ({Type}) dead-lettered")]
    private static partial void LogDeadLettered(ILogger logger, Guid messageId, string type, Exception exception);
}
