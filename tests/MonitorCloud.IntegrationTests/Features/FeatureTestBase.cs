using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.IntegrationTests.Features;

/// <summary>A fresh host and database per test class, with the two-customer test world.</summary>
public abstract class FeatureTestBase(SqlServerFixture sql) : IAsyncLifetime
{
    protected TestApp App { get; } = new(sql);

    protected TestWorld World { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        await App.InitializeAsync();
        await App.ResetDatabaseAsync();
        World = await TestWorld.CreateAsync(App);
    }

    public virtual Task DisposeAsync() => App.DisposeAsync();

    protected static object Credentials(string email, string password = TestWorld.Password) => new { email, password };
}
