using MediatR;
using Microsoft.Extensions.Caching.Memory;
using MonitorCloud.Application.Abstractions.Caching;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Behaviors;

/// <summary>6. Caches successful results of <see cref="ICacheableQuery"/> queries per tenant and location scope.</summary>
public sealed class QueryCachingBehavior<TRequest, TResponse>(IMemoryCache cache, ITenantContext tenantContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (request is not ICacheableQuery cacheable)
            return await next(cancellationToken);

        var key = BuildKey(typeof(TRequest), cacheable.CacheKey, tenantContext);
        if (cache.TryGetValue(key, out TResponse? cached) && cached is not null)
            return cached;

        var response = await next(cancellationToken);
        if (response.IsSuccess)
            cache.Set(key, response, cacheable.CacheDuration);

        return response;
    }

    internal static string BuildKey(Type requestType, string queryKey, ITenantContext scope)
    {
        var tenant = scope.TenantId?.ToString("N") ?? (scope.IsUnrestricted ? "platform" : "none");
        var locations = scope.LocationScope.Count == 0
            ? "all"
            : string.Join(',', scope.LocationScope.Order().Select(l => l.ToString("N")));
        return $"q:{requestType.FullName}:t:{tenant}:l:{locations}:{queryKey}";
    }
}
