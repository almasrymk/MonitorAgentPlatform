namespace MonitorCloud.Infrastructure.Messaging;

/// <summary>An event written in the business transaction and dispatched later (01 section 6).</summary>
public sealed class OutboxMessage
{
    public const int MaxAttempts = 8;

    private OutboxMessage()
    {
        Type = string.Empty;
        Payload = string.Empty;
    }

    public Guid Id { get; private set; }
    public string Type { get; private set; }
    public string Payload { get; private set; }
    public Guid? TenantId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? LastError { get; private set; }
    public bool DeadLettered { get; private set; }

    public static OutboxMessage Create(Guid id, string type, string payload, Guid? tenantId, DateTimeOffset occurredAt) =>
        new()
        {
            Id = id,
            Type = type,
            Payload = payload,
            TenantId = tenantId,
            OccurredAt = occurredAt,
            NextAttemptAt = occurredAt,
        };

    public void MarkProcessed(DateTimeOffset now)
    {
        ProcessedAt = now;
        LastError = null;
    }

    /// <summary>Exponential back-off (2, 4, 8 ... seconds); dead-lettered after <see cref="MaxAttempts"/> attempts.</summary>
    public void MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length <= 2000 ? error : error[..2000];
        if (Attempts >= MaxAttempts)
        {
            DeadLettered = true;
            return;
        }

        NextAttemptAt = now.AddSeconds(Math.Pow(2, Attempts));
    }
}

/// <summary>Records that a handler processed a message, so it never runs twice.</summary>
public sealed class InboxMessage
{
    private InboxMessage()
    {
        Handler = string.Empty;
    }

    public Guid MessageId { get; private set; }
    public string Handler { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }

    public static InboxMessage Create(Guid messageId, string handler, DateTimeOffset processedAt) =>
        new() { MessageId = messageId, Handler = handler, ProcessedAt = processedAt };
}
