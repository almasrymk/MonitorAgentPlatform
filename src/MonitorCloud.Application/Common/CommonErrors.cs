using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Common;

/// <summary>Errors produced by the pipeline and shared across modules.</summary>
public static class CommonErrors
{
    public static readonly Error Unauthorized = Error.Unauthorized(ErrorCodes.AuthUnauthorized, "Authentication is required.");
    public static readonly Error Forbidden = Error.Forbidden(ErrorCodes.AuthForbidden, "You do not have permission to perform this action.");
    public static readonly Error TenantScopeRequired = Error.Validation(ErrorCodes.TenantScopeRequired, "Select a customer workspace first.");
    public static readonly Error EntitlementExpired = Error.Forbidden(ErrorCodes.EntitlementExpired, "The subscription has expired.");
    public static readonly Error ConcurrencyConflict = Error.Conflict(ErrorCodes.ConcurrencyConflict, "The record was changed by someone else. Reload and try again.");

    public static Error FeatureNotEntitled(string feature) =>
        Error.Forbidden(ErrorCodes.FeatureNotEntitled, $"The current plan does not include '{feature}'.").With("feature", feature);

    public static Error ValidationFailed(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        new(ErrorCodes.ValidationFailed, "One or more fields are invalid.", ErrorKind.Validation) { FieldErrors = fieldErrors };

    public static Error InvalidSort(string sort, IEnumerable<string> allowed) =>
        ValidationFailed(new Dictionary<string, string[]> { ["sort"] = [$"Unknown sort '{sort}'. Allowed: {string.Join(", ", allowed)}."] });
}
