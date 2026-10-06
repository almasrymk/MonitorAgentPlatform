using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.Api.Controllers;

namespace MonitorCloud.IntegrationTests.Endpoints;

/// <summary>One HTTP endpoint of the running API, read from <see cref="EndpointDataSource"/>.</summary>
public sealed partial record ApiEndpoint(string Method, string Route, EndpointMetadataCollection Metadata)
{
    public bool AllowsAnonymous => Metadata.GetMetadata<IAllowAnonymous>() is not null;

    public bool IsPlatform => Route.StartsWith("/api/v1/platform/", StringComparison.OrdinalIgnoreCase);

    public bool IsAuth => Route.StartsWith("/api/v1/auth/", StringComparison.OrdinalIgnoreCase);

    public bool IsAgent => Route.StartsWith("/api/agent/", StringComparison.OrdinalIgnoreCase);

    public bool IsBusiness => Route.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);

    /// <summary>A tenant-scope business endpoint (not platform, not auth, not agent).</summary>
    public bool IsTenant => IsBusiness && !IsPlatform && !IsAuth && !IsAgent;

    public bool IsList => Metadata.GetMetadata<ListEndpointAttribute>() is not null;

    public bool HasParameters => Route.Contains('{', StringComparison.Ordinal);

    public string Key => $"{Method} {Route}";

    /// <summary>The route with every parameter replaced by <paramref name="id"/>.</summary>
    public string Url(Guid id) => RouteParameter().Replace(Route, id.ToString());

    public override string ToString() => Key;

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();
}

public static class EndpointCatalog
{
    /// <summary>Endpoints reachable without a token (09 section 4). Any other anonymous endpoint fails the suite.</summary>
    public static readonly string[] AnonymousAllowList =
    [
        "POST /api/v1/auth/login",
        "POST /api/v1/auth/refresh",
        "POST /api/v1/auth/logout",
        "POST /api/v1/auth/invitations/accept",
        "POST /api/agent/v1/enroll",
        "POST /api/agent/v1/token",
        "GET /health/live",
        "GET /health/ready",
        "GET /openapi/{documentName}.json",
    ];

    public static IReadOnlyList<ApiEndpoint> All(IServiceProvider services)
    {
        var source = services.GetRequiredService<EndpointDataSource>();
        return source.Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(m => new ApiEndpoint(m, "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'), e.Metadata)))
            .Where(e => e.Method != HttpMethods.Options && e.Method != HttpMethods.Head)
            .DistinctBy(e => e.Key)
            .OrderBy(e => e.Route, StringComparer.Ordinal)
            .ThenBy(e => e.Method, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<ApiEndpoint> Business(IServiceProvider services) =>
        All(services).Where(e => e.IsBusiness).ToList();
}
