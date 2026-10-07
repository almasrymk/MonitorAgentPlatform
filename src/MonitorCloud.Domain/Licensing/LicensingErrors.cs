using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Licensing;

public static class LicensingErrors
{
    public static readonly Error Unavailable = Error.Unavailable("LICENSING_UNAVAILABLE", "The licensing service is not reachable. Try again later.");
    public static readonly Error CustomerNotFound = Error.NotFound("LICENSING_CUSTOMER_NOT_FOUND", "The customer does not exist in the Licensing Platform.");
    public static readonly Error RateLimited = Error.TooManyRequests("RATE_LIMITED", "Too many requests to the licensing service.");
}
