namespace MonitorCloud.Application.Configuration.Contracts;

/// <summary>Raises the configuration version of a device after a change outside the document (monitor points).</summary>
public interface IConfigurationVersioning
{
    /// <summary>Current unit of work; creates the configuration first when the device has none.</summary>
    Task BumpAsync(Guid deviceId, Guid tenantId, Guid? userId, CancellationToken ct);

    /// <summary>The target version of a device (0 when it has no configuration yet).</summary>
    Task<int> VersionAsync(Guid deviceId, CancellationToken ct);
}

public sealed record AgentConfiguration(int Version, string Json);

/// <summary>The complete <c>ConfigUpdate</c> document of a device, for the gateway.</summary>
public interface IAgentConfigurationReader
{
    Task<AgentConfiguration?> GetAsync(Guid deviceId, CancellationToken ct);
}
