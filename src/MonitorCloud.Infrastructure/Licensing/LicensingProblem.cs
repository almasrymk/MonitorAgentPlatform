using MonitorCloud.SharedKernel;

namespace MonitorCloud.Infrastructure.Licensing;

/// <summary>Licensing errors with the Licensing Platform's codes, passed through unchanged (04 section 4.1).</summary>
public static class LicensingProblem
{
    public static readonly Error InvalidLicense = Error.NotFound("LIC_INVALID_LICENSE", "The license is not valid.");
    public static readonly Error Suspended = Error.Forbidden("LIC_SUSPENDED", "The license is suspended.");
    public static readonly Error Revoked = Error.Forbidden("LIC_REVOKED", "The license has been revoked.");
    public static readonly Error Expired = Error.Forbidden("LIC_EXPIRED", "The license has expired.");
    public static readonly Error SubscriptionExpired = Error.Forbidden("LIC_SUBSCRIPTION_EXPIRED", "The subscription for this license has expired.");
    public static readonly Error SubscriptionInactive = Error.Forbidden("LIC_SUBSCRIPTION_INACTIVE", "The subscription for this license is not active.");
    public static readonly Error ActivationLimitReached = Error.Conflict("LIC_ACTIVATION_LIMIT_REACHED", "The maximum number of activations has been reached.");
    public static readonly Error DeviceNotActivated = Error.NotFound("LIC_DEVICE_NOT_ACTIVATED", "This device is not activated for the license.");
    public static readonly Error InvalidDevice = Error.Validation("LIC_INVALID_DEVICE", "Device id must be 8-128 characters of letters, digits, '-', '_', ':' or '.'.");

    /// <summary>Maps an HTTP status and problem code to an error, keeping the code.</summary>
    public static Error FromStatus(int status, string? code, string? message) => new(
        string.IsNullOrWhiteSpace(code) ? (status >= 500 ? "LICENSING_UNAVAILABLE" : "LICENSING_ERROR") : code,
        message ?? "Licensing Platform error.",
        status switch
        {
            400 => ErrorKind.Validation,
            401 => ErrorKind.Unauthorized,
            403 => ErrorKind.Forbidden,
            404 => ErrorKind.NotFound,
            409 => ErrorKind.Conflict,
            410 => ErrorKind.Gone,
            423 => ErrorKind.Locked,
            429 => ErrorKind.TooManyRequests,
            >= 500 => ErrorKind.Unavailable,
            _ => ErrorKind.Validation,
        });

    public static bool IsValidDeviceId(string? deviceId) =>
        deviceId is { Length: >= 8 and <= 128 } && deviceId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.');
}
