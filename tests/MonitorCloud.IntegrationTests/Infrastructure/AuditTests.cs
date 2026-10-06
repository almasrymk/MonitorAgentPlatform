using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Domain.Audit;
using MonitorCloud.Infrastructure.Persistence;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Infrastructure;

[Collection(SqlCollection.Name)]
public sealed class AuditTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly TestApp _app = new(sql);

    public async Task InitializeAsync()
    {
        await _app.InitializeAsync();
        await _app.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task Added_records_are_written_with_the_unit_of_work()
    {
        await _app.InSystemScopeAsync(async sp =>
        {
            sp.GetRequiredService<IAuditLogger>().Add("tenant.created", "Tenant", "t-1", "details");
            await sp.GetRequiredService<AppDbContext>().SaveChangesAsync();
        });

        var record = await _app.InSystemScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Set<AuditRecord>().SingleAsync());
        record.Action.ShouldBe("tenant.created");
        record.ActorType.ShouldBe(AuditActorType.System);
        record.At.ShouldBe(_app.Clock.GetUtcNow());
    }

    [Fact]
    public async Task Added_records_are_discarded_when_the_unit_of_work_is_not_saved()
    {
        await _app.InSystemScopeAsync(sp =>
        {
            sp.GetRequiredService<IAuditLogger>().Add("tenant.created", "Tenant", "t-1");
            return Task.CompletedTask;
        });

        var count = await _app.InSystemScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Set<AuditRecord>().CountAsync());
        count.ShouldBe(0);
    }

    [Fact]
    public async Task WriteNow_persists_immediately_even_if_the_business_change_is_discarded()
    {
        await _app.InSystemScopeAsync(async sp =>
        {
            var logger = sp.GetRequiredService<IAuditLogger>();
            logger.Add("ignored", "Tenant", "t-1");
            await logger.WriteNowAsync("auth.login.failed", "User", "u-1", "bad password", success: false, CancellationToken.None);
        });

        var records = await _app.InSystemScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Set<AuditRecord>().ToListAsync());
        records.ShouldHaveSingleItem().Action.ShouldBe("auth.login.failed");
        records[0].Success.ShouldBeFalse();
    }
}
