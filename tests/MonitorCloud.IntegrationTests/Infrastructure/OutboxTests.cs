using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Domain.Audit;
using MonitorCloud.Infrastructure.Messaging;
using MonitorCloud.Infrastructure.Persistence;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Infrastructure;

public sealed record SampleHappenedV1(Guid SampleId, DateTimeOffset At) : DomainEvent(At);

public sealed record SampleFailsV1(Guid SampleId, DateTimeOffset At) : DomainEvent(At);

/// <summary>Records every invocation so tests can count handler runs.</summary>
public sealed class HandlerLog
{
    public ConcurrentQueue<(string Handler, Guid EventId)> Calls { get; } = new();
}

public sealed class SampleHandlerOne(HandlerLog log, AppDbContext db, TimeProvider clock) : IIntegrationEventHandler<SampleHappenedV1>
{
    public Task HandleAsync(SampleHappenedV1 integrationEvent, CancellationToken cancellationToken)
    {
        log.Calls.Enqueue((nameof(SampleHandlerOne), integrationEvent.EventId));
        // Effects are saved by the dispatcher together with the inbox row.
        db.Add(AuditRecord.Create(null, AuditActorType.System, null, null, "sample.handled", "Sample", integrationEvent.SampleId.ToString(), true, null, null, null, clock.GetUtcNow()));
        return Task.CompletedTask;
    }
}

public sealed class SampleHandlerTwo(HandlerLog log) : IIntegrationEventHandler<SampleHappenedV1>
{
    public Task HandleAsync(SampleHappenedV1 integrationEvent, CancellationToken cancellationToken)
    {
        log.Calls.Enqueue((nameof(SampleHandlerTwo), integrationEvent.EventId));
        return Task.CompletedTask;
    }
}

public sealed class FailingHandler(HandlerLog log) : IIntegrationEventHandler<SampleFailsV1>
{
    public Task HandleAsync(SampleFailsV1 integrationEvent, CancellationToken cancellationToken)
    {
        log.Calls.Enqueue((nameof(FailingHandler), integrationEvent.EventId));
        throw new InvalidOperationException("handler failed");
    }
}

public sealed class OutboxTestApp(SqlServerFixture sql) : TestApp(sql)
{
    public HandlerLog Log { get; } = new();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton(Log);
        services.AddScoped<IIntegrationEventHandler<SampleHappenedV1>, SampleHandlerOne>();
        services.AddScoped<IIntegrationEventHandler<SampleHappenedV1>, SampleHandlerTwo>();
        services.AddScoped<IIntegrationEventHandler<SampleFailsV1>, FailingHandler>();
        services.RemoveAll<EventTypeRegistry>();
        services.AddSingleton(new EventTypeRegistry([typeof(SampleHappenedV1).Assembly]));
    }
}

[Collection(SqlCollection.Name)]
public sealed class OutboxTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly OutboxTestApp _app = new(sql);

    public async Task InitializeAsync()
    {
        await _app.InitializeAsync();
        await _app.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => _app.DisposeAsync();

    private OutboxDispatcher Dispatcher => _app.Services.GetRequiredService<OutboxDispatcher>();

    private Task Enqueue(IDomainEvent domainEvent) => _app.InSystemScopeAsync(async sp =>
    {
        var db = sp.GetRequiredService<AppDbContext>();
        db.OutboxMessages.Add(OutboxSerializer.ToMessage(domainEvent, null, _app.Clock));
        await db.SaveChangesAsync();
    });

    private Task<OutboxMessage> Load(Guid id) =>
        _app.InSystemScopeAsync(sp => sp.GetRequiredService<AppDbContext>().OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id));

    [Fact]
    public async Task Every_handler_runs_once_and_the_message_is_marked_processed()
    {
        var domainEvent = new SampleHappenedV1(Guid.CreateVersion7(), _app.Clock.GetUtcNow());
        await Enqueue(domainEvent);

        var taken = await Dispatcher.DispatchBatchAsync(CancellationToken.None);

        taken.ShouldBe(1);
        _app.Log.Calls.Select(c => c.Handler).ShouldBe([nameof(SampleHandlerOne), nameof(SampleHandlerTwo)], ignoreOrder: true);
        (await Load(domainEvent.EventId)).ProcessedAt.ShouldNotBeNull();
        var inbox = await _app.InSystemScopeAsync(sp => sp.GetRequiredService<AppDbContext>().InboxMessages.CountAsync(i => i.MessageId == domainEvent.EventId));
        inbox.ShouldBe(2);
        var audits = await _app.InSystemScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Set<AuditRecord>().CountAsync(a => a.Action == "sample.handled"));
        audits.ShouldBe(1);
    }

    [Fact]
    public async Task Inbox_prevents_a_handler_from_running_twice()
    {
        var domainEvent = new SampleHappenedV1(Guid.CreateVersion7(), _app.Clock.GetUtcNow());
        await Enqueue(domainEvent);
        await _app.InSystemScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            db.InboxMessages.Add(InboxMessage.Create(domainEvent.EventId, typeof(SampleHandlerOne).FullName!, _app.Clock.GetUtcNow()));
            await db.SaveChangesAsync();
        });

        await Dispatcher.DispatchBatchAsync(CancellationToken.None);

        _app.Log.Calls.Select(c => c.Handler).ShouldBe([nameof(SampleHandlerTwo)]);
    }

    [Fact]
    public async Task Failing_handler_is_retried_with_back_off_and_dead_lettered_after_eight_attempts()
    {
        var domainEvent = new SampleFailsV1(Guid.CreateVersion7(), _app.Clock.GetUtcNow());
        await Enqueue(domainEvent);

        await Dispatcher.DispatchBatchAsync(CancellationToken.None);
        var afterFirst = await Load(domainEvent.EventId);
        afterFirst.Attempts.ShouldBe(1);
        afterFirst.NextAttemptAt.ShouldBe(_app.Clock.GetUtcNow().AddSeconds(2));
        afterFirst.LastError.ShouldNotBeNull().ShouldContain("handler failed");

        // Not due yet: nothing is taken.
        (await Dispatcher.DispatchBatchAsync(CancellationToken.None)).ShouldBe(0);

        for (var attempt = 2; attempt <= OutboxMessage.MaxAttempts; attempt++)
        {
            _app.Clock.Advance(TimeSpan.FromMinutes(10));
            (await Dispatcher.DispatchBatchAsync(CancellationToken.None)).ShouldBe(1);
        }

        var final = await Load(domainEvent.EventId);
        final.DeadLettered.ShouldBeTrue();
        final.Attempts.ShouldBe(OutboxMessage.MaxAttempts);
        final.ProcessedAt.ShouldBeNull();

        _app.Clock.Advance(TimeSpan.FromHours(1));
        (await Dispatcher.DispatchBatchAsync(CancellationToken.None)).ShouldBe(0);
        _app.Log.Calls.Count.ShouldBe(OutboxMessage.MaxAttempts);
    }

    [Fact]
    public async Task Unknown_event_types_are_retried_and_reported()
    {
        var id = Guid.CreateVersion7();
        await _app.InSystemScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            db.OutboxMessages.Add(OutboxMessage.Create(id, "Unknown.EventV9", "{}", null, _app.Clock.GetUtcNow()));
            await db.SaveChangesAsync();
        });

        await Dispatcher.DispatchBatchAsync(CancellationToken.None);

        var message = await Load(id);
        message.Attempts.ShouldBe(1);
        message.LastError.ShouldNotBeNull().ShouldContain("Unknown event type");
    }
}
