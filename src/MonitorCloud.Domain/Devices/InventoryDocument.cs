using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Devices;

public enum InventoryKind
{
    Hardware,
    Os,
    Network,
    Disks,
    Programs,
    Services,
    Users,
    Sensors,
}

/// <summary>A compressed inventory document (02 section 4). Written only when its hash changes.</summary>
public sealed class InventoryDocument : Entity, ITenantOwned
{
    private InventoryDocument()
    {
        Json = [];
        Hash = string.Empty;
    }

    public Guid DeviceId { get; private set; }
    public Guid TenantId { get; private set; }
    public InventoryKind Kind { get; private set; }

    /// <summary>Brotli-compressed UTF-8 JSON.</summary>
    public byte[] Json { get; private set; }

    public string Hash { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static InventoryDocument Create(Guid deviceId, Guid tenantId, InventoryKind kind, byte[] json, string hash, DateTimeOffset now) =>
        new()
        {
            DeviceId = Guard.NotEmpty(deviceId, nameof(DeviceId)),
            TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
            Kind = kind,
            Json = json ?? [],
            Hash = Guard.NotEmpty(hash, nameof(Hash), 64),
            UpdatedAt = now,
        };

    /// <summary>Returns false when the content did not change.</summary>
    public bool Replace(byte[] json, string hash, DateTimeOffset now)
    {
        if (string.Equals(Hash, hash, StringComparison.Ordinal))
            return false;
        Json = json ?? [];
        Hash = Guard.NotEmpty(hash, nameof(Hash), 64);
        UpdatedAt = now;
        return true;
    }
}
