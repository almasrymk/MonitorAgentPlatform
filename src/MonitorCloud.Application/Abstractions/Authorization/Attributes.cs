namespace MonitorCloud.Application.Abstractions.Authorization;

/// <summary>The caller needs this permission. Unless the request is also <see cref="PlatformOnlyAttribute"/>, it runs in a tenant scope.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequirePermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}

/// <summary>Only platform users in the unrestricted platform scope (no workspace selected).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class PlatformOnlyAttribute : Attribute;

/// <summary>The request is sent on behalf of an authenticated device (agent gateway).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AllowDeviceAttribute : Attribute;

/// <summary>No caller identity required (sign-in, refresh, enrollment).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AllowAnonymousRequestAttribute : Attribute;

/// <summary>Any signed-in user, without a specific permission (own profile, sign-out).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AllowAuthenticatedUserAttribute : Attribute;

/// <summary>Requests run only by background jobs under <c>RunAsSystem</c> or <c>RunAsTenant</c>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SystemOnlyAttribute : Attribute;

/// <summary>The tenant's plan must include this feature code.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequiresFeatureAttribute(string feature) : Attribute
{
    public string Feature { get; } = feature;
}

public static class AuthorizationAttributes
{
    /// <summary>Every MediatR request must carry exactly one of these (architecture test).</summary>
    public static readonly IReadOnlyList<Type> Declarations =
    [
        typeof(RequirePermissionAttribute),
        typeof(PlatformOnlyAttribute),
        typeof(AllowDeviceAttribute),
        typeof(AllowAnonymousRequestAttribute),
        typeof(AllowAuthenticatedUserAttribute),
        typeof(SystemOnlyAttribute),
    ];
}
