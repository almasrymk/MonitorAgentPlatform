namespace MonitorCloud.Application.Abstractions.Audit;

/// <summary>The actor of a record written before the caller is signed in (sign-in itself): the user it concerns.</summary>
public sealed record AuditActor(Guid UserId, string Name, Guid? TenantId);

public interface IAuditLogger
{
    /// <summary>Adds an audit record to the current unit of work (written with the business change).</summary>
    void Add(string action, string entityType, string? entityId, string? details = null, bool success = true, AuditActor? actor = null);

    /// <summary>Writes immediately in its own transaction, for failures whose business transaction rolls back.</summary>
    Task WriteNowAsync(string action, string entityType, string? entityId, string? details, bool success, CancellationToken cancellationToken, AuditActor? actor = null);
}
