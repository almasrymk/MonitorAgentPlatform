using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Email;
using MonitorCloud.Application.Identity;
using MonitorCloud.Domain.Identity;
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
    public CapturingEmailSender Mail { get; } = new();
    public string ConnectionString { get; } = sql.NewDatabase();

    /// <summary>Files and Data Protection keys of this app (a fresh temp folder).</summary>
    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "monitorcloud-tests", Guid.NewGuid().ToString("N"));

    public virtual Task InitializeAsync()
    {
        // Building the server runs Program, which applies the migrations.
        _ = Server;
        return Task.CompletedTask;
    }

    public new virtual async Task DisposeAsync()
    {
        await base.DisposeAsync();
        try
        {
            if (Directory.Exists(StorageRoot))
                Directory.Delete(StorageRoot, recursive: true);
        }
        catch (IOException)
        {
            // A file still open by the OS; the temp folder is cleaned up eventually.
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Monitor", ConnectionString);
        builder.UseSetting("Seed:ApplyMigrations", "true");
        builder.UseSetting("Seed:DemoData", "false");
        builder.UseSetting("Storage:Root", StorageRoot);
        builder.UseSetting("Outbox:Enabled", "false");
        builder.UseSetting("Jwt:SigningKey", UserSigningKey);
        builder.UseSetting("Jwt:DeviceSigningKey", DeviceSigningKey);
        builder.UseSetting("Cors:Origins:0", "http://localhost:4300");
        builder.UseSetting("RateLimiting:Auth:PermitLimit", "100000");
        builder.UseSetting("Licensing:Mode", "Fake");
        builder.UseSetting("Licensing:SyncEnabled", "false");
        builder.UseSetting("Licensing:FakeDataPath", "");
        builder.UseSetting("Agent:BackgroundJobsEnabled", "false");
        builder.UseSetting("Gateway:PresenceEnabled", "false");
        builder.UseSetting("Telemetry:JobsEnabled", "false");
        builder.UseSetting("Gateway:HelloTimeoutSeconds", "2");
        builder.UseSetting("RateLimiting:Enroll:PermitLimit", "100000");
        builder.UseSetting("RateLimiting:AgentToken:PermitLimit", "100000");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel:Default"] = "Warning",
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Mail);
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

    public Task<T> InDbAsync<T>(Func<AppDbContext, Task<T>> action) =>
        InSystemScopeAsync(sp => action(sp.GetRequiredService<AppDbContext>()));

    /// <summary>Clears every table (dependency order), so tests in a class do not share data.</summary>
    public Task ResetDatabaseAsync() => InSystemScopeAsync(sp => TestDatabase.ResetAsync(sp.GetRequiredService<AppDbContext>()));

    /// <summary>A user access token issued at the test clock's time.</summary>
    public string TokenFor(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Services.GetRequiredService<ITokenService>().CreateAccessToken(user, Clock.GetUtcNow()).Token;
    }

    /// <summary>An HTTP client signed in as <paramref name="user"/>, optionally inside a customer workspace.</summary>
    public HttpClient ClientFor(User? user, Guid? workspace = null)
    {
        var client = CreateClient();
        if (user is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenFor(user));
        if (workspace is { } tenantId)
            client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
        return client;
    }

    public HttpClient ClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
