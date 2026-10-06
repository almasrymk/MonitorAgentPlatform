using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Tenancy;

public static class TenancyErrors
{
    public static readonly Error TenantNotFound = Error.NotFound("TENANT_NOT_FOUND", "Customer not found.");
    public static readonly Error InvalidTransition = Error.Conflict("TENANT_INVALID_TRANSITION", "The customer cannot change to that status.");
    public static readonly Error TenantCodeTaken = Error.Conflict("TENANT_CODE_TAKEN", "Another customer already uses this code.");
    public static readonly Error LocationNotFound = Error.NotFound("LOCATION_NOT_FOUND", "Location not found.");
    public static readonly Error LocationIsDefault = Error.Conflict("LOCATION_IS_DEFAULT", "The default location cannot be deleted or closed.");
    public static readonly Error LocationNotEmpty = Error.Conflict("LOCATION_NOT_EMPTY", "The location still has devices.");
    public static readonly Error LocationCodeTaken = Error.Conflict("LOCATION_CODE_TAKEN", "Another location already uses this code.");
}
