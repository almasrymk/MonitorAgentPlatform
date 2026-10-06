using System.Security.Claims;
using MonitorCloud.Application.Abstractions.Context;

namespace MonitorCloud.Api.Infrastructure;

/// <summary>
/// The caller from the HTTP request's claims (03 section 1). Outside an HTTP request (background jobs) the actor is
/// <see cref="ActorType.System"/>.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string TenantClaim = "tid";
    public const string LocationsClaim = "loc";
    public const string PermissionClaim = "perm";
    public const string TypeClaim = "typ";
    public const string RoleClaim = "role";
    public const string PlatformClaimValue = "platform";

    private HttpContext? Context => accessor.HttpContext;
    private ClaimsPrincipal? Principal => Context?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public ActorType ActorType => Context is null
        ? ActorType.System
        : string.Equals(Principal?.FindFirstValue(TypeClaim), "device", StringComparison.Ordinal) ? ActorType.Device : ActorType.User;

    public Guid? UserId => ActorType == ActorType.User ? ParseGuid(Principal?.FindFirstValue("sub")) : null;
    public Guid? DeviceId => ActorType == ActorType.Device ? ParseGuid(Principal?.FindFirstValue("sub")) : null;
    public Guid? TenantId => ParseGuid(Principal?.FindFirstValue(TenantClaim));
    public string? Role => Principal?.FindFirstValue(RoleClaim);
    public string? Name => Principal?.FindFirstValue("name") ?? Principal?.FindFirstValue("email");
    public bool IsPlatform => IsAuthenticated && ActorType == ActorType.User && TenantId is null && Role is not null;

    public IReadOnlyCollection<Guid> LocationScope =>
        (Principal?.FindFirstValue(LocationsClaim) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseGuid)
            .OfType<Guid>()
            .ToArray();

    public bool HasPermission(string permission) =>
        Principal?.Claims.Any(c => c.Type == PermissionClaim && string.Equals(c.Value, permission, StringComparison.Ordinal)) == true;

    public string? IpAddress => Context?.Connection.RemoteIpAddress?.ToString();
    public string? CorrelationId => Context?.Items[CorrelationIdMiddleware.ItemKey] as string;

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}
