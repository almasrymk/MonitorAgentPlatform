using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.Infrastructure.Persistence;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Infrastructure;

[Collection(SqlCollection.Name)]
public sealed class MigrationTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly TestApp _app = new(sql);

    public Task InitializeAsync() => _app.InitializeAsync();

    public Task DisposeAsync() => _app.DisposeAsync();

    [Fact]
    public async Task Migrations_are_applied_to_an_empty_database_at_start_up()
    {
        var pending = await _app.InSystemScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Database.GetPendingMigrationsAsync());

        pending.ShouldBeEmpty();
    }

    [Fact]
    public async Task Applying_migrations_again_is_a_no_op()
    {
        await _app.InSystemScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Database.MigrateAsync());
        var applied = await _app.InSystemScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Database.GetAppliedMigrationsAsync());

        applied.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Module_schemas_exist()
    {
        var schemas = await _app.InSystemScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Database
            .SqlQueryRaw<string>("SELECT name AS [Value] FROM sys.schemas")
            .ToListAsync());

        schemas.ShouldContain(Schemas.Messaging);
        schemas.ShouldContain(Schemas.Audit);
    }

    [Fact]
    public async Task Reset_script_clears_every_table()
    {
        await _app.InSystemScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            db.OutboxMessages.Add(MonitorCloud.Infrastructure.Messaging.OutboxMessage.Create(Guid.CreateVersion7(), "T", "{}", null, _app.Clock.GetUtcNow()));
            await db.SaveChangesAsync();
        });

        await _app.ResetDatabaseAsync();

        var count = await _app.InSystemScopeAsync(sp => sp.GetRequiredService<AppDbContext>().OutboxMessages.CountAsync());
        count.ShouldBe(0);
    }
}
