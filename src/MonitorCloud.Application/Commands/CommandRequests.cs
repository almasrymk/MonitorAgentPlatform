using System.Text.Json;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Commands;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Entitlements;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Configuration.Contracts;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Domain.Commands;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Commands;

public static partial class CommandErrors
{
    public static readonly Error NotFound = Error.NotFound(ErrorCodes.CommandNotFound, "Command not found.");
    public static readonly Error RemoteActionsDisabled = Error.Conflict(ErrorCodes.RemoteActionsDisabled, "Remote actions are turned off in this device's settings.");
    public static readonly Error DeviceUnlicensed = Error.Conflict(ErrorCodes.DeviceUnlicensed, "The device has no licence.");

    /// <summary>A Windows service or systemd/launchd unit name.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 ._@-]{0,99}$")]
    public static partial Regex ServiceName();
}

public sealed record DeviceCommandDto(
    Guid Id, string Type, string? Service, string Reason, string RequestedByName, DateTimeOffset RequestedAt, DateTimeOffset ExpiresAt, string Status, DateTimeOffset? SentAt,
    DateTimeOffset? CompletedAt, string? Output);

/// <summary>Whether the Remote Actions menu can be offered for a device (07 section 2, rule 10), and why not.</summary>
public sealed record RemoteActionsDto(bool Available, string? Reason);

/// <summary>A command as the gateway sends it, signed.</summary>
public sealed record SignedCommand(Guid Id, string Type, string ParametersJson, DateTimeOffset ExpiresAt, string Nonce, byte[] Signature, string KeyId);

internal static class CommandDtos
{
    public static DeviceCommandDto From(DeviceCommand c) =>
        new(c.Id, c.Type, Service(c.ParametersJson), c.Reason, c.RequestedByName, c.RequestedAt, c.ExpiresAt, c.Status.ToString(), c.SentAt, c.CompletedAt, c.Output);

    private static string? Service(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("service", out var service) ? service.GetString() : null;
    }

    public static SignedCommand Sign(DeviceCommand c, ICommandSigner signer) =>
        new(c.Id, c.Type, c.ParametersJson, c.ExpiresAt, c.Nonce, signer.Sign(c.SigningPayload()), signer.KeyId);
}

// ---------------------------------------------------------------- request (portal)

/// <summary>
/// A remote action (05 section 9): plan feature <c>remote.actions</c>, permission <c>devices.manage</c> and
/// <c>features.remoteActions</c> in the device configuration; a reason is required and audited.
/// </summary>
[RequirePermission(Permissions.DevicesManage)]
[RequiresFeature(Features.RemoteActions)]
public sealed record SendDeviceCommandCommand(Guid DeviceId, string Type, string? Service, string Reason) : ICommand<DeviceCommandDto>;

internal sealed class SendDeviceCommandCommandValidator : AbstractValidator<SendDeviceCommandCommand>
{
    public SendDeviceCommandCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.Type).Must(t => DeviceCommand.Types.Contains(t)).WithMessage("Unknown command type.");
        RuleFor(x => x.Service).NotEmpty().Must(s => s is not null && CommandErrors.ServiceName().IsMatch(s)).When(x => x.Type.StartsWith("service-", StringComparison.Ordinal))
            .WithMessage("A service command needs a valid service name.");
        RuleFor(x => x.Service).Empty().When(x => !x.Type.StartsWith("service-", StringComparison.Ordinal)).WithMessage("Only service commands take a service name.");
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(10).MaximumLength(500);
    }
}

internal sealed class SendDeviceCommandCommandHandler(
    IAppDbContext db, IDeviceDirectory devices, IRemoteActionsSetting setting, ICurrentUser user, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<SendDeviceCommandCommand, DeviceCommandDto>
{
    public async Task<Result<DeviceCommandDto>> Handle(SendDeviceCommandCommand request, CancellationToken cancellationToken)
    {
        var device = await devices.FindAsync(request.DeviceId, cancellationToken);
        if (device is null)
            return Error.NotFound(ErrorCodes.DeviceNotFound, "Device not found.");
        if (!device.Licensed)
            return CommandErrors.DeviceUnlicensed;
        if (!await setting.EnabledAsync(device.Id, cancellationToken))
            return CommandErrors.RemoteActionsDisabled;
        var parameters = request.Service is { Length: > 0 } service ? JsonSerializer.Serialize(new { service }) : "{}";
        var command = DeviceCommand.Request(device.TenantId, device.Id, device.LocationId, request.Type, parameters, request.Reason.Trim(), user.UserId ?? Guid.Empty,
            user.Name ?? "User", clock.GetUtcNow());
        db.Set<DeviceCommand>().Add(command);
        audit.Add("device.command.requested", "Device", device.Id.ToString(), $"{command.Type}{(request.Service is null ? string.Empty : $" {request.Service}")}: {command.Reason}");
        return CommandDtos.From(command);
    }
}

[RequirePermission(Permissions.DevicesRead)]
public sealed record GetDeviceCommandsQuery(Guid DeviceId, int? Page, int? PageSize) : IQuery<PagedResult<DeviceCommandDto>>;

internal sealed class GetDeviceCommandsQueryValidator : AbstractValidator<GetDeviceCommandsQuery>
{
    public GetDeviceCommandsQueryValidator() => RuleFor(x => x.DeviceId).NotEmpty();
}

internal sealed class GetDeviceCommandsQueryHandler(IReadDbContext db, IDeviceDirectory devices) : IQueryHandler<GetDeviceCommandsQuery, PagedResult<DeviceCommandDto>>
{
    public async Task<Result<PagedResult<DeviceCommandDto>>> Handle(GetDeviceCommandsQuery request, CancellationToken cancellationToken)
    {
        if (await devices.FindAsync(request.DeviceId, cancellationToken) is null)
            return Error.NotFound(ErrorCodes.DeviceNotFound, "Device not found.");
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);
        var query = db.Query<DeviceCommand>().Where(c => c.DeviceId == request.DeviceId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(c => c.RequestedAt).ThenBy(c => c.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<DeviceCommandDto>([.. rows.Select(CommandDtos.From)], total, page, pageSize);
    }
}

/// <summary>For the Remote Actions menu: the feature, the licence and the device setting (the permission is checked by the portal).</summary>
[RequirePermission(Permissions.DevicesRead)]
public sealed record GetRemoteActionsQuery(Guid DeviceId) : IQuery<RemoteActionsDto>;

internal sealed class GetRemoteActionsQueryValidator : AbstractValidator<GetRemoteActionsQuery>
{
    public GetRemoteActionsQueryValidator() => RuleFor(x => x.DeviceId).NotEmpty();
}

internal sealed class GetRemoteActionsQueryHandler(IDeviceDirectory devices, IRemoteActionsSetting setting, IEntitlementReader entitlements, ITenantContext scope)
    : IQueryHandler<GetRemoteActionsQuery, RemoteActionsDto>
{
    public async Task<Result<RemoteActionsDto>> Handle(GetRemoteActionsQuery request, CancellationToken cancellationToken)
    {
        var device = await devices.FindAsync(request.DeviceId, cancellationToken);
        if (device is null)
            return Error.NotFound(ErrorCodes.DeviceNotFound, "Device not found.");
        var plan = await entitlements.GetAsync(scope.TenantId ?? device.TenantId, cancellationToken);
        if (!plan.Has(Features.RemoteActions))
            return new RemoteActionsDto(false, "feature");
        if (!device.Licensed)
            return new RemoteActionsDto(false, "unlicensed");
        return await setting.EnabledAsync(device.Id, cancellationToken) ? new RemoteActionsDto(true, null) : new RemoteActionsDto(false, "setting");
    }
}

// ---------------------------------------------------------------- gateway side

/// <summary>The device's open, unexpired commands, signed, oldest first (sent after Welcome and on request).</summary>
[AllowDevice]
public sealed record GetPendingCommandsQuery(Guid DeviceId, Guid TenantId) : IQuery<IReadOnlyList<SignedCommand>>;

internal sealed class GetPendingCommandsQueryValidator : AbstractValidator<GetPendingCommandsQuery>
{
    public GetPendingCommandsQueryValidator() => RuleFor(x => x.DeviceId).NotEmpty();
}

internal sealed class GetPendingCommandsQueryHandler(IReadDbContext db, ITenantScopeSetter scope, ICommandSigner signer, TimeProvider clock)
    : IQueryHandler<GetPendingCommandsQuery, IReadOnlyList<SignedCommand>>
{
    public async Task<Result<IReadOnlyList<SignedCommand>>> Handle(GetPendingCommandsQuery request, CancellationToken cancellationToken)
    {
        scope.RunAsTenant(request.TenantId);
        var now = clock.GetUtcNow();
        var open = await db.Query<DeviceCommand>()
            .Where(c => c.DeviceId == request.DeviceId && (c.Status == CommandStatus.Pending || c.Status == CommandStatus.Sent) && c.ExpiresAt > now)
            .OrderBy(c => c.RequestedAt)
            .ToListAsync(cancellationToken);
        return open.Select(c => CommandDtos.Sign(c, signer)).ToList();
    }
}

/// <summary>The gateway wrote the commands to the device's stream.</summary>
[AllowDevice]
public sealed record MarkCommandsSentCommand(Guid DeviceId, Guid TenantId, IReadOnlyList<Guid> CommandIds) : ICommand;

internal sealed class MarkCommandsSentCommandValidator : AbstractValidator<MarkCommandsSentCommand>
{
    public MarkCommandsSentCommandValidator() => RuleFor(x => x.CommandIds).NotEmpty();
}

internal sealed class MarkCommandsSentCommandHandler(IAppDbContext db, ITenantScopeSetter scope, TimeProvider clock) : ICommandHandler<MarkCommandsSentCommand>
{
    public async Task<Result> Handle(MarkCommandsSentCommand request, CancellationToken cancellationToken)
    {
        scope.RunAsTenant(request.TenantId);
        var ids = request.CommandIds;
        var commands = await db.Set<DeviceCommand>().Where(c => c.DeviceId == request.DeviceId && ids.Contains(c.Id)).ToListAsync(cancellationToken);
        foreach (var command in commands)
            command.MarkSent(clock.GetUtcNow());
        return Result.Success();
    }
}

/// <summary><c>CommandResult</c> from the agent (05 section 9): recorded once and audited.</summary>
[AllowDevice]
public sealed record RecordCommandResultCommand(Guid DeviceId, Guid TenantId, Guid CommandId, CommandStatus Status, string? Output) : ICommand;

internal sealed class RecordCommandResultCommandValidator : AbstractValidator<RecordCommandResultCommand>
{
    public RecordCommandResultCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty();
        RuleFor(x => x.Status).Must(s => s is CommandStatus.Succeeded or CommandStatus.Failed or CommandStatus.Rejected or CommandStatus.Expired).WithMessage("A result must be final.");
    }
}

internal sealed class RecordCommandResultCommandHandler(IAppDbContext db, ITenantScopeSetter scope, IAuditLogger audit, TimeProvider clock) : ICommandHandler<RecordCommandResultCommand>
{
    public async Task<Result> Handle(RecordCommandResultCommand request, CancellationToken cancellationToken)
    {
        scope.RunAsTenant(request.TenantId);
        var command = await db.Set<DeviceCommand>().SingleOrDefaultAsync(c => c.Id == request.CommandId && c.DeviceId == request.DeviceId, cancellationToken);
        if (command is null)
            return CommandErrors.NotFound;
        var completed = command.Complete(request.Status, request.Output, clock.GetUtcNow());
        if (completed.IsFailure)
            return completed;
        audit.Add("device.command.completed", "Device", request.DeviceId.ToString(), $"{command.Type}: {request.Status}", request.Status == CommandStatus.Succeeded);
        return Result.Success();
    }
}

/// <summary>Commands without an answer after their expiry are closed as expired and audited (system scope).</summary>
public sealed class CommandExpiryService(IAppDbContext db, IUnitOfWork unitOfWork, IAuditLogger audit, TimeProvider clock)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var cutoff = now.AddMinutes(-1);
        var overdue = await db.Set<DeviceCommand>()
            .Where(c => (c.Status == CommandStatus.Pending || c.Status == CommandStatus.Sent) && c.ExpiresAt < cutoff)
            .Take(500)
            .ToListAsync(ct);
        foreach (var command in overdue.Where(c => c.ExpireIfOverdue(now)))
            audit.Add("device.command.expired", "Device", command.DeviceId.ToString(), command.Type, success: false);
        await unitOfWork.SaveChangesAsync(ct);
        return overdue.Count;
    }
}
