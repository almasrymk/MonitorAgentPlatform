using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Common;

/// <summary>Errors produced by the pipeline and shared across modules.</summary>
public static class CommonErrors
{
    public static readonly Error Unauthorized = Error.Unauthorized("AUTH_UNAUTHORIZED", "Authentication is required.");
    public static readonly Error Forbidden = Error.Forbidden("AUTH_FORBIDDEN", "You do not have permission to perform this action.");
    public static readonly Error TenantScopeRequired = Error.Forbidden("TENANT_SCOPE_REQUIRED", "Select a customer workspace first.");
    public static readonly Error EntitlementExpired = Error.Forbidden("ENTITLEMENT_EXPIRED", "The subscription has expired.");

    public static Error FeatureNotEntitled(string feature) =>
        Error.Forbidden("FEATURE_NOT_ENTITLED", $"The current plan does not include '{feature}'.");

    public static Error ValidationFailed(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        new("VALIDATION_FAILED", "One or more fields are invalid.", ErrorKind.Validation) { FieldErrors = fieldErrors };
}
