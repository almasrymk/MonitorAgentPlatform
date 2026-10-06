using System.Collections.Concurrent;
using System.Reflection;
using MediatR;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Common;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Behaviors;

/// <summary>2. Checks the request's authorization attribute against the caller and the tenant scope.</summary>
public sealed class AuthorizationBehavior<TRequest, TResponse>(ICurrentUser currentUser, ITenantContext tenantContext)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        var error = Authorize(RequestAuthorization.For(typeof(TRequest)), currentUser, tenantContext);
        return error is null ? await next(cancellationToken) : ResultFactory.Failure<TResponse>(error);
    }

    internal static Error? Authorize(RequestAuthorization rules, ICurrentUser user, ITenantContext scope)
    {
        if (rules.AllowAnonymous)
            return null;

        // Background jobs run as System and are trusted; the tenant scope they chose still filters data.
        if (user.ActorType == ActorType.System)
            return null;

        if (!user.IsAuthenticated)
            return CommonErrors.Unauthorized;

        if (rules.SystemOnly)
            return CommonErrors.Forbidden;

        if (user.ActorType == ActorType.Device)
            return rules.AllowDevice ? null : CommonErrors.Forbidden;

        if (rules.AllowDevice && rules.Permissions.Count == 0)
            return CommonErrors.Forbidden;

        if (rules.PlatformOnly && (!user.IsPlatform || !scope.IsUnrestricted))
            return CommonErrors.Forbidden;

        foreach (var permission in rules.Permissions)
        {
            if (!user.HasPermission(permission))
                return CommonErrors.Forbidden;
        }

        // Tenant endpoints need a tenant scope: platform users must select a workspace (X-Tenant-Id).
        if (rules.Permissions.Count > 0 && !rules.PlatformOnly && scope.TenantId is null)
            return CommonErrors.TenantScopeRequired;

        if (!rules.Declared)
            return CommonErrors.Forbidden;

        return null;
    }
}

/// <summary>The authorization attributes of a request type, read once.</summary>
internal sealed record RequestAuthorization(
    bool Declared,
    bool AllowAnonymous,
    bool AllowAuthenticated,
    bool AllowDevice,
    bool PlatformOnly,
    bool SystemOnly,
    IReadOnlyList<string> Permissions)
{
    private static readonly ConcurrentDictionary<Type, RequestAuthorization> Cache = new();

    public static RequestAuthorization For(Type requestType) => Cache.GetOrAdd(requestType, Read);

    private static RequestAuthorization Read(Type type)
    {
        var permissions = type.GetCustomAttributes<RequirePermissionAttribute>(false).Select(a => a.Permission).ToArray();
        var allowAnonymous = type.IsDefined(typeof(AllowAnonymousRequestAttribute), false);
        var allowAuthenticated = type.IsDefined(typeof(AllowAuthenticatedUserAttribute), false);
        var allowDevice = type.IsDefined(typeof(AllowDeviceAttribute), false);
        var platformOnly = type.IsDefined(typeof(PlatformOnlyAttribute), false);
        var systemOnly = type.IsDefined(typeof(SystemOnlyAttribute), false);
        var declared = allowAnonymous || allowAuthenticated || allowDevice || platformOnly || systemOnly || permissions.Length > 0;
        return new RequestAuthorization(declared, allowAnonymous, allowAuthenticated, allowDevice, platformOnly, systemOnly, permissions);
    }
}
