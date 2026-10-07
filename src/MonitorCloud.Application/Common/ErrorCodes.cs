namespace MonitorCloud.Application.Common;

/// <summary>
/// Every stable error code of the API (06 section 6). The portal translates by code: each constant has an entry
/// <c>error.{CODE}</c> in <c>portal/src/app/core/i18n/en.json</c> and <c>ar.json</c> (checked by a test).
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string AuthUnauthorized = "AUTH_UNAUTHORIZED";
    public const string AuthForbidden = "AUTH_FORBIDDEN";
    public const string AuthInvalidCredentials = "AUTH_INVALID_CREDENTIALS";
    public const string AuthLocked = "AUTH_LOCKED";
    public const string AuthTenantSuspended = "AUTH_TENANT_SUSPENDED";
    public const string AuthRefreshInvalid = "AUTH_REFRESH_INVALID";
    public const string AuthInvitationInvalid = "AUTH_INVITATION_INVALID";
    public const string AuthPasswordIncorrect = "AUTH_PASSWORD_INCORRECT";
    public const string AuthPasswordWeak = "AUTH_PASSWORD_WEAK";
    public const string TenantScopeRequired = "TENANT_SCOPE_REQUIRED";
    public const string TenantNotFound = "TENANT_NOT_FOUND";
    public const string TenantInvalidTransition = "TENANT_INVALID_TRANSITION";
    public const string TenantCodeTaken = "TENANT_CODE_TAKEN";
    public const string LocationNotFound = "LOCATION_NOT_FOUND";
    public const string LocationNotEmpty = "LOCATION_NOT_EMPTY";
    public const string LocationIsDefault = "LOCATION_IS_DEFAULT";
    public const string LocationCodeTaken = "LOCATION_CODE_TAKEN";
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string UserEmailTaken = "USER_EMAIL_TAKEN";
    public const string UserLastAdmin = "USER_LAST_ADMIN";
    public const string UserInvalidTransition = "USER_INVALID_TRANSITION";
    public const string UserRoleNotAllowed = "USER_ROLE_NOT_ALLOWED";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string FeatureNotEntitled = "FEATURE_NOT_ENTITLED";
    public const string EntitlementExpired = "ENTITLEMENT_EXPIRED";
    public const string RateLimited = "RATE_LIMITED";
    public const string NotFound = "NOT_FOUND";
    public const string InternalError = "INTERNAL_ERROR";
    public const string LicensingUnavailable = "LICENSING_UNAVAILABLE";
    public const string LicensingCustomerNotFound = "LICENSING_CUSTOMER_NOT_FOUND";
    public const string LicInvalidLicense = "LIC_INVALID_LICENSE";
    public const string LicSuspended = "LIC_SUSPENDED";
    public const string LicRevoked = "LIC_REVOKED";
    public const string LicExpired = "LIC_EXPIRED";
    public const string LicSubscriptionExpired = "LIC_SUBSCRIPTION_EXPIRED";
    public const string LicSubscriptionInactive = "LIC_SUBSCRIPTION_INACTIVE";
    public const string LicActivationLimitReached = "LIC_ACTIVATION_LIMIT_REACHED";
    public const string LicDeviceNotActivated = "LIC_DEVICE_NOT_ACTIVATED";
    public const string LicInvalidDevice = "LIC_INVALID_DEVICE";
    public const string DeviceNotFound = "DEVICE_NOT_FOUND";
    public const string DeviceInvalidCredential = "DEVICE_INVALID_CREDENTIAL";
    public const string DeviceRetired = "DEVICE_RETIRED";
    public const string DeviceUnlicensed = "DEVICE_UNLICENSED";
    public const string EnrollInvalidLocationCode = "ENROLL_INVALID_LOCATION_CODE";
    public const string EnrollTenantNotActive = "ENROLL_TENANT_NOT_ACTIVE";
    public const string EnrollProtocolUnsupported = "ENROLL_PROTOCOL_UNSUPPORTED";
    public const string EnrollmentCodeNotFound = "ENROLLMENT_CODE_NOT_FOUND";
    public const string CredentialRevoked = "CREDENTIAL_REVOKED";
    public const string TenantArchived = "TENANT_ARCHIVED";
    public const string ProtocolUnsupported = "PROTOCOL_UNSUPPORTED";

    public static IReadOnlyList<string> All { get; } = typeof(ErrorCodes)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral)
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();
}
