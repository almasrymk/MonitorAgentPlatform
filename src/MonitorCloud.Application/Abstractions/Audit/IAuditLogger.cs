namespace MonitorCloud.Application.Abstractions.Audit;

public interface IAuditLogger
{
    /// <summary>Adds an audit record to the current unit of work (written with the business change).</summary>
    void Add(string action, string entityType, string? entityId, string? details = null, bool success = true);

    /// <summary>Writes immediately in its own transaction, for failures whose business transaction rolls back.</summary>
    Task WriteNowAsync(string action, string entityType, string? entityId, string? details, bool success, CancellationToken cancellationToken);
}
