using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Infrastructure.Persistence;

namespace MonitorCloud.TestShared;

/// <summary>
/// The real API host on its own SQL Server database (created from the migrations once per test class).
/// Background loops are off; tests drive jobs explicitly with the <see cref="Clock"/>.
/// </summary>
public class TestApp(SqlServerFixture sql) : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string UserSigningKey = "TEST-ONLY-user-signing-key-0123456789abcdef";
    public const string DeviceSigningKey = "TEST-ONLY-device-signing-key-0123456789abcdef";

    public TestClock Clock { get; } = new();
    public string ConnectionString { get; } = sql.NewDatabase();

    public virtual Task InitializeAsync()
    {
        // Building the server runs Program, which applies the migrations.
        _ = Server;
        return Task.CompletedTask;
    }

    public new virtual async Task DisposeAsync() => await base.DisposeAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Monitor", ConnectionString);
        builder.UseSetting("Seed:ApplyMigrations", "true");
        builder.UseSetting("Seed:DemoData", "false");
        builder.UseSetting("Outbox:Enabled", "false");
        builder.UseSetting("Jwt:SigningKey", UserSigningKey);
        builder.UseSetting("Jwt:DeviceSigningKey", DeviceSigningKey);
        builder.UseSetting("Cors:Origins:0", "http://localhost:4300");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel:Default"] = "Warning",
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            ConfigureTestServices(services);
        });
    }

    /// <summary>Hook for suites that need extra services (test event handlers, fakes).</summary>
    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    /// <summary>Runs <paramref name="action"/> in a new DI scope with the system (unrestricted) tenant scope.</summary>
    public async Task<T> InSystemScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantScopeSetter>().RunAsSystem();
        return await action(scope.ServiceProvider);
    }

    public Task InSystemScopeAsync(Func<IServiceProvider, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return InSystemScopeAsync<bool>(async sp =>
        {
            await action(sp);
            return true;
        });
    }

    /// <summary>Clears every table (dependency order), so tests in a class do not share data.</summary>
    public Task ResetDatabaseAsync() => InSystemScopeAsync(sp => TestDatabase.ResetAsync(sp.GetRequiredService<AppDbContext>()));
}
