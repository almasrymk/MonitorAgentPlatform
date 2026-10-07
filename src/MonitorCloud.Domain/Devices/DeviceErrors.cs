using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Devices;

public static class DeviceErrors
{
    public static readonly Error NotFound = Error.NotFound("DEVICE_NOT_FOUND", "Device not found.");
    public static readonly Error InvalidCredential = Error.Unauthorized("DEVICE_INVALID_CREDENTIAL", "The device credential is not valid.");
    public static readonly Error Retired = Error.Conflict("DEVICE_RETIRED", "The device is retired.");
    public static readonly Error Unlicensed = Error.Forbidden("DEVICE_UNLICENSED", "This action is locked for an unlicensed device.");
    public static readonly Error InvalidLocationCode = Error.Validation("ENROLL_INVALID_LOCATION_CODE", "The location code is not valid.");
    public static readonly Error TenantNotActive = Error.Forbidden("ENROLL_TENANT_NOT_ACTIVE", "The customer account is not active.");
    public static readonly Error ProtocolUnsupported = Error.Validation("ENROLL_PROTOCOL_UNSUPPORTED", "The agent protocol version is not supported.");

    /// <summary>Same code as the Tenancy error: the target location of a move does not exist in the device's tenant.</summary>
    public static readonly Error LocationNotFound = Error.NotFound("LOCATION_NOT_FOUND", "Location not found.");
}
