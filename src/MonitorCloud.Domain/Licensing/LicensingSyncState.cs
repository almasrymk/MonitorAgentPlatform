using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Licensing;

/// <summary>Single row: where the entitlement sync is in the Licensing change feed (02 section 3).</summary>
public sealed class LicensingSyncState : Entity
{
    public static readonly Guid SingletonId = new("00000000-0000-7000-8000-000000000001");

    private LicensingSyncState()
    {
    }

    public string? Cursor { get; private set; }
    public DateTimeOffset? LastSuccessAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? LastFullReconcileAt { get; private set; }
    public string? LastError { get; private set; }
    public int ConsecutiveFailures { get; private set; }

    public static LicensingSyncState Create() => new() { Id = SingletonId };

    public void Succeeded(string? cursor, DateTimeOffset now, bool fullReconcile = false)
    {
        Cursor = cursor;
        LastAttemptAt = now;
        LastSuccessAt = now;
        LastError = null;
        ConsecutiveFailures = 0;
        if (fullReconcile)
            LastFullReconcileAt = now;
    }

    public void Failed(string error, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(error);
        LastAttemptAt = now;
        LastError = error.Length <= 1000 ? error : error[..1000];
        ConsecutiveFailures++;
    }

    public bool FullReconcileDue(DateTimeOffset now, TimeSpan interval) =>
        LastFullReconcileAt is null || now - LastFullReconcileAt.Value >= interval;
}
