namespace MonitorCloud.Application.Identity;

public sealed record UserProfileDto(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    Guid? TenantId,
    string? TenantName,
    IReadOnlyList<string> Permissions,
    string Language,
    IReadOnlyList<Guid> LocationIds);

public sealed record AuthResultDto(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken, UserProfileDto User);

public sealed record UserListItemDto(
    Guid Id,
    string FullName,
    string Email,
    string Role,
    string RoleName,
    string PermissionSummary,
    string Status,
    DateTimeOffset? LastLoginAt,
    IReadOnlyList<Guid> LocationIds,
    string Version);

public sealed record RoleDto(string Code, string Name, string PermissionSummary, IReadOnlyList<string> Permissions);
