using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Audit;

public enum AuditActorType
{
    User,
    Device,
    System,
}

/// <summary>Append-only audit trail (02 section 11). There is no update or delete path.</summary>
public sealed class AuditRecord : Entity, IOptionallyTenantOwned
{
    public const int ActionMaxLength = 100;
    public const int EntityTypeMaxLength = 64;
    public const int EntityIdMaxLength = 64;
    public const int DetailsMaxLength = 2000;

    private AuditRecord()
    {
        ActorName = string.Empty;
        Action = string.Empty;
        EntityType = string.Empty;
    }

    public Guid? TenantId { get; private set; }
    public AuditActorType ActorType { get; private set; }
    public Guid? ActorId { get; private set; }
    public string ActorName { get; private set; }
    public string Action { get; private set; }
    public string EntityType { get; private set; }
    public string? EntityId { get; private set; }
    public bool Success { get; private set; }
    public string? Details { get; private set; }
    public string? Ip { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset At { get; private set; }

    public static AuditRecord Create(
        Guid? tenantId,
        AuditActorType actorType,
        Guid? actorId,
        string? actorName,
        string action,
        string entityType,
        string? entityId,
        bool success,
        string? details,
        string? ip,
        string? correlationId,
        DateTimeOffset at) =>
        new()
        {
            TenantId = tenantId,
            ActorType = actorType,
            ActorId = actorId,
            ActorName = Truncate(actorName, 200) ?? actorType.ToString(),
            Action = Guard.NotEmpty(action, nameof(Action), ActionMaxLength),
            EntityType = Guard.NotEmpty(entityType, nameof(EntityType), EntityTypeMaxLength),
            EntityId = Truncate(entityId, EntityIdMaxLength),
            Success = success,
            Details = Truncate(details, DetailsMaxLength),
            Ip = Truncate(ip, 64),
            CorrelationId = Truncate(correlationId, 64),
            At = at,
        };

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
