namespace MonitorCloud.Application.Tenancy.Contracts;

public sealed record LocationInfo(Guid Id, Guid TenantId, string Name, string Code, string? City, string? Country, string TimeZone, bool IsDefault);

/// <summary>Locations by id or default, for the Devices module.</summary>
public interface ILocationLookup
{
    Task<LocationInfo?> DefaultAsync(Guid tenantId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, LocationInfo>> GetAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken ct);
}

/// <summary>Redeems a location enrollment code during enrollment (adds one use in the current unit of work).</summary>
public interface IEnrollmentCodeRedeemer
{
    Task<Guid?> RedeemAsync(Guid tenantId, string code, CancellationToken ct);
}

/// <summary>Tenant names for lists across modules.</summary>
public interface ITenantNames
{
    Task<IReadOnlyDictionary<Guid, string>> GetAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken ct);
}
