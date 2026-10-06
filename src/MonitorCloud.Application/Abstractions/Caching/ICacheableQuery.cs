namespace MonitorCloud.Application.Abstractions.Caching;

/// <summary>A query whose successful result may be cached. The key is completed with the tenant and location scope.</summary>
public interface ICacheableQuery
{
    string CacheKey { get; }
    TimeSpan CacheDuration { get; }
}
