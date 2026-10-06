namespace MonitorCloud.Application.Common;

public static class TimeZones
{
    /// <summary>True for an IANA (or Windows) time-zone id known to the host.</summary>
    public static bool IsValid(string? id) => !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
}
