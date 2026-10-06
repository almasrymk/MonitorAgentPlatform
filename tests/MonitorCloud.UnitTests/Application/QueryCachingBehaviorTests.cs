using Microsoft.Extensions.Caching.Memory;
using MonitorCloud.Application.Behaviors;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.UnitTests.Application;

public sealed class QueryCachingBehaviorTests : IDisposable
{
    private static readonly Guid TenantA = Guid.CreateVersion7();
    private static readonly Guid TenantB = Guid.CreateVersion7();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private int _calls;

    public void Dispose() => _cache.Dispose();

    private Task<Result<int>> Run(DashboardQuery query, TestTenantContext scope, Result<int>? result = null)
    {
        var behavior = new QueryCachingBehavior<DashboardQuery, Result<int>>(_cache, scope);
        return behavior.Handle(query, _ =>
        {
            _calls++;
            return Task.FromResult(result ?? Result<int>.Ok(_calls));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Second_call_in_the_same_scope_is_served_from_the_cache()
    {
        var first = await Run(new DashboardQuery("d"), TestTenantContext.ForTenant(TenantA));
        var second = await Run(new DashboardQuery("d"), TestTenantContext.ForTenant(TenantA));

        _calls.ShouldBe(1);
        second.Value.ShouldBe(first.Value);
    }

    [Fact]
    public async Task Results_are_not_shared_between_tenants()
    {
        await Run(new DashboardQuery("d"), TestTenantContext.ForTenant(TenantA));
        var other = await Run(new DashboardQuery("d"), TestTenantContext.ForTenant(TenantB));

        _calls.ShouldBe(2);
        other.Value.ShouldBe(2);
    }

    [Fact]
    public async Task Results_are_not_shared_between_location_scopes()
    {
        var location = Guid.CreateVersion7();
        await Run(new DashboardQuery("d"), TestTenantContext.ForTenant(TenantA));
        await Run(new DashboardQuery("d"), TestTenantContext.ForTenant(TenantA, location));

        _calls.ShouldBe(2);
    }

    [Fact]
    public async Task Different_query_keys_are_cached_separately()
    {
        await Run(new DashboardQuery("7"), TestTenantContext.ForTenant(TenantA));
        await Run(new DashboardQuery("30"), TestTenantContext.ForTenant(TenantA));

        _calls.ShouldBe(2);
    }

    [Fact]
    public async Task Failures_are_not_cached()
    {
        await Run(new DashboardQuery("d"), TestTenantContext.ForTenant(TenantA), Error.NotFound("X_Y", "missing"));
        await Run(new DashboardQuery("d"), TestTenantContext.ForTenant(TenantA));

        _calls.ShouldBe(2);
    }

    [Fact]
    public void Key_contains_the_tenant_and_the_sorted_location_scope()
    {
        var l1 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var l2 = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var key = QueryCachingBehavior<DashboardQuery, Result<int>>.BuildKey(typeof(DashboardQuery), "k", TestTenantContext.ForTenant(TenantA, l1, l2));

        key.ShouldContain(TenantA.ToString("N"));
        key.IndexOf(l2.ToString("N"), StringComparison.Ordinal).ShouldBeLessThan(key.IndexOf(l1.ToString("N"), StringComparison.Ordinal));
        QueryCachingBehavior<DashboardQuery, Result<int>>.BuildKey(typeof(DashboardQuery), "k", TestTenantContext.Platform()).ShouldContain(":t:platform:");
    }

    [Fact]
    public async Task Non_cacheable_queries_pass_through()
    {
        var behavior = new QueryCachingBehavior<ReadDevicesQuery, Result<string>>(_cache, TestTenantContext.ForTenant(TenantA));
        var calls = 0;

        for (var i = 0; i < 2; i++)
        {
            await behavior.Handle(new ReadDevicesQuery("x"), _ =>
            {
                calls++;
                return Task.FromResult(Result<string>.Ok("x"));
            }, CancellationToken.None);
        }

        calls.ShouldBe(2);
    }
}
