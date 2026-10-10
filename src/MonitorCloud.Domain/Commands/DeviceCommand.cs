using System.Security.Cryptography;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Commands;

public enum CommandStatus
{
    Pending,
    Sent,
    Succeeded,
    Failed,
    Rejected,
    Expired,
}

/// <summary>A remote action requested by a user (05 section 9); the agent answers with a <c>CommandResult</c>.</summary>
public sealed record DeviceCommandRequestedV1(Guid CommandId, Guid DeviceId, Guid TenantId, DateTimeOffset At) : DomainEvent(At);

/// <summary>The agent answered (or the command expired); audited and shown in the history.</summary>
public sealed record DeviceCommandCompletedV1(Guid CommandId, Guid DeviceId, Guid TenantId, string Type, CommandStatus Status, DateTimeOffset At) : DomainEvent(At);

/// <summary>
/// A signed remote action for one device (05 section 9): requested by a user with a reason, sent by the gateway while
/// it has not expired (at most 5 minutes), answered by the agent once.
/// </summary>
public sealed class DeviceCommand : AggregateRoot, ITenantOwned
{
    public const int MaxOutputLength = 64 * 1024;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>The six command types; the service ones need a <c>service</c> parameter.</summary>
    public static readonly IReadOnlyList<string> Types = ["refresh-inventory", "run-speed-test", "restart-agent", "service-start", "service-stop", "service-restart"];

    public static readonly Error InvalidType = Error.Validation("COMMAND_TYPE_UNKNOWN", "Unknown command type.");
    public static readonly Error AlreadyCompleted = Error.Conflict("COMMAND_COMPLETED", "The command has already been answered.");

    private DeviceCommand()
    {
        Type = string.Empty;
        ParametersJson = "{}";
        Reason = string.Empty;
        RequestedByName = string.Empty;
        Nonce = string.Empty;
    }

    public Guid TenantId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid LocationId { get; private set; }
    public string Type { get; private set; }
    public string ParametersJson { get; private set; }
    public string Reason { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string RequestedByName { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>128 random bits; the agent refuses a nonce it has seen in the last 10 minutes.</summary>
    public string Nonce { get; private set; }
    public CommandStatus Status { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? Output { get; private set; }

    public bool IsOpen => Status is CommandStatus.Pending or CommandStatus.Sent;

    public static DeviceCommand Request(
        Guid tenantId, Guid deviceId, Guid locationId, string type, string parametersJson, string reason, Guid userId, string userName, DateTimeOffset now)
    {
        Guard.Against(!Types.Contains(type), InvalidType);
        var command = new DeviceCommand
        {
            TenantId = Guard.NotEmpty(tenantId, nameof(TenantId)),
            DeviceId = Guard.NotEmpty(deviceId, nameof(DeviceId)),
            LocationId = locationId,
            Type = type,
            ParametersJson = Guard.NotEmpty(parametersJson, nameof(ParametersJson), 2000),
            Reason = Guard.NotEmpty(reason, nameof(Reason), 500),
            RequestedByUserId = userId,
            RequestedByName = Guard.NotEmpty(userName, nameof(RequestedByName), 200),
            RequestedAt = now,
            // Whole milliseconds: the signed payload carries the expiry as Unix milliseconds.
            ExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(now.Add(Lifetime).ToUnixTimeMilliseconds()),
            Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            Status = CommandStatus.Pending,
        };
        command.Raise(new DeviceCommandRequestedV1(command.Id, deviceId, tenantId, now));
        return command;
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;

    /// <summary>The gateway wrote it to the device's stream.</summary>
    public void MarkSent(DateTimeOffset now)
    {
        if (Status == CommandStatus.Pending)
        {
            Status = CommandStatus.Sent;
            SentAt = now;
        }
    }

    /// <summary>The agent's answer. Only the first answer counts; a late answer after expiry is still recorded.</summary>
    public Result Complete(CommandStatus status, string? output, DateTimeOffset now)
    {
        if (status is CommandStatus.Pending or CommandStatus.Sent)
            return Error.Validation(Guard.ValidationCode, "A result must be final.");
        if (!IsOpen && Status != CommandStatus.Expired)
            return AlreadyCompleted;
        Status = status;
        Output = output is null ? null : output.Length <= MaxOutputLength ? output : output[..MaxOutputLength];
        CompletedAt = now;
        Raise(new DeviceCommandCompletedV1(Id, DeviceId, TenantId, Type, status, now));
        return Result.Success();
    }

    /// <summary>No answer before <see cref="ExpiresAt"/> (plus a grace minute): the agent refuses it anyway.</summary>
    public bool ExpireIfOverdue(DateTimeOffset now)
    {
        if (!IsOpen || now < ExpiresAt.AddMinutes(1))
            return false;
        Status = CommandStatus.Expired;
        CompletedAt = now;
        Raise(new DeviceCommandCompletedV1(Id, DeviceId, TenantId, Type, CommandStatus.Expired, now));
        return true;
    }

    /// <summary>
    /// The UTF-8 text the cloud signs and the agent verifies (05 section 9):
    /// <c>command_id|type|parameters_json|expires_at|nonce|device_id</c>, ids as 32 hex digits, the expiry as Unix milliseconds.
    /// </summary>
    public string SigningPayload() => Payload(Id, Type, ParametersJson, ExpiresAt, Nonce, DeviceId);

    public static string Payload(Guid commandId, string type, string parametersJson, DateTimeOffset expiresAt, string nonce, Guid deviceId) =>
        string.Join('|', commandId.ToString("N"), type, parametersJson, expiresAt.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture), nonce, deviceId.ToString("N"));
}
