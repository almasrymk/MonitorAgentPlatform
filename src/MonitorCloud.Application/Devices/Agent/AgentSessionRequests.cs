using Microsoft.EntityFrameworkCore;
using FluentValidation;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Abstractions.Realtime;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Application.Tenancy.Contracts;
using Microsoft.Extensions.Options;
using MonitorCloud.Domain.Devices;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Devices.Agent;

/// <summary>Why a session is refused; the gateway sends the code as <c>Disconnect.code</c> (05 section 2).</summary>
public static class AgentSessionErrors
{
    public static readonly Error DeviceRetired = Error.Forbidden("DEVICE_RETIRED", "The device is retired.");
    public static readonly Error CredentialRevoked = Error.Unauthorized("CREDENTIAL_REVOKED", "The device credential is revoked.");
    public static readonly Error TenantArchived = Error.Forbidden("TENANT_ARCHIVED", "The customer account is archived.");
    public static readonly Error ProtocolUnsupported = Error.Validation("PROTOCOL_UNSUPPORTED", "The agent protocol version is not supported.");
}

public sealed record AgentHello(
    int ProtocolVersion, string? AgentVersion, string Hostname, OsFamily OsFamily, string? OsName, string? OsVersion, string? Architecture,
    int AppliedConfigVersion, string? LocalIp, string? MacAddress);

/// <summary>What the gateway needs for <c>Welcome</c>.</summary>
public sealed record AgentSessionStart(Guid LocationId, ulong LastReceivedSequence, int ConfigVersion, bool Licensed, bool Restricted = false);

/// <summary>
/// A device opened its stream and sent Hello: checks device, credential and tenant, updates the agent facts, marks it
/// Online. The device and tenant come from the device token only (05, rule of the proto file).
/// </summary>
[AllowDevice]
public sealed record OpenAgentSessionCommand(Guid DeviceId, Guid TenantId, AgentHello Hello, string? PublicIp) : ICommand<AgentSessionStart>;

internal sealed class OpenAgentSessionCommandValidator : AbstractValidator<OpenAgentSessionCommand>
{
    public OpenAgentSessionCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Hello.Hostname).NotEmpty().MaximumLength(200);
    }
}

internal sealed class OpenAgentSessionCommandHandler(
    IAppDbContext db, IUnitOfWork unitOfWork, ITenantScopeSetter scope, ITenantDirectory tenants, IDeviceSeats seats, ILicensingPolicy policy,
    IOptions<AgentSettings> agent, ILiveNotifier live, Configuration.Contracts.IConfigurationVersioning configurations, TimeProvider clock)
    : ICommandHandler<OpenAgentSessionCommand, AgentSessionStart>
{
    public async Task<Result<AgentSessionStart>> Handle(OpenAgentSessionCommand request, CancellationToken cancellationToken)
    {
        var hello = request.Hello;
        if (hello.ProtocolVersion < agent.Value.SupportedProtocolMin || hello.ProtocolVersion > agent.Value.SupportedProtocolMax)
            return AgentSessionErrors.ProtocolUnsupported;

        scope.RunAsTenant(request.TenantId);
        var tenant = await tenants.FindAsync(request.TenantId, cancellationToken);
        if (tenant is null || tenant.Status == "Archived")
            return AgentSessionErrors.TenantArchived;
        var device = await db.Set<Device>().SingleOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken);
        if (device is null || device.IsRetired)
            return AgentSessionErrors.DeviceRetired;
        var credential = await db.Set<DeviceCredential>().SingleOrDefaultAsync(c => c.DeviceId == device.Id, cancellationToken);
        if (credential is null || credential.IsRevoked)
            return AgentSessionErrors.CredentialRevoked;

        var now = clock.GetUtcNow();
        device.UpdateAgentInfo(new AgentInfo(hello.Hostname, hello.OsFamily, hello.OsName, hello.OsVersion, hello.Architecture, hello.AgentVersion,
            hello.ProtocolVersion, hello.LocalIp, request.PublicIp, hello.MacAddress));
        var state = await db.Set<DeviceState>().SingleAsync(s => s.DeviceId == device.Id, cancellationToken);
        var wasOffline = state.Connection == ConnectionState.Offline;
        state.SetOsFamily(hello.OsFamily);
        state.SetAppliedConfigVersion(hello.AppliedConfigVersion);
        state.SetConnection(ConnectionState.Online, now, policy.UnlicensedGrace);
        if (wasOffline)
            device.CameOnline(now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await live.DeviceStateChangedAsync([DeviceLive.Change(state)], cancellationToken);

        var seat = await seats.FindAsync(device.Id, cancellationToken);
        var restricted = state.LicenseState == LicenseStateValue.Unlicensed && state.UnlicensedSince is { } since && now >= since.Add(policy.UnlicensedGrace);
        var configVersion = await configurations.VersionAsync(device.Id, cancellationToken);
        return new AgentSessionStart(state.LocationId, (ulong)Math.Max(0, state.LastEventSequence), configVersion, (seat?.State ?? "Licensed") == "Licensed", restricted);
    }
}

public sealed record OfflineDevice(Guid DeviceId, Guid TenantId, string Reason);

/// <summary>
/// Marks devices Offline (presence monitor: missed heartbeats or a closed stream; agent Goodbye). Raises
/// <c>DeviceWentOfflineV1</c> once per transition.
/// </summary>
[AllowDevice]
public sealed record MarkDevicesOfflineCommand(IReadOnlyList<OfflineDevice> Devices) : ICommand;

internal sealed class MarkDevicesOfflineCommandValidator : AbstractValidator<MarkDevicesOfflineCommand>
{
    public MarkDevicesOfflineCommandValidator() => RuleFor(x => x.Devices).NotEmpty();
}

internal sealed class MarkDevicesOfflineCommandHandler(
    IAppDbContext db, IUnitOfWork unitOfWork, ITenantScopeSetter scope, ICurrentUser caller, ILicensingPolicy policy, ILiveNotifier live, TimeProvider clock)
    : ICommandHandler<MarkDevicesOfflineCommand>
{
    public async Task<Result> Handle(MarkDevicesOfflineCommand request, CancellationToken cancellationToken)
    {
        var devices = request.Devices;
        if (caller.ActorType == ActorType.Device)
        {
            // An agent may only report itself (Goodbye).
            if (devices.Any(d => d.DeviceId != caller.DeviceId || d.TenantId != caller.TenantId))
                return Error.Forbidden("AUTH_FORBIDDEN", "A device can only report itself.");
            scope.RunAsTenant(caller.TenantId!.Value);
        }
        else
        {
            scope.RunAsSystem();
        }

        var ids = devices.Select(d => d.DeviceId).ToList();
        var states = await db.Set<DeviceState>().Where(s => ids.Contains(s.DeviceId) && s.Connection == ConnectionState.Online).ToListAsync(cancellationToken);
        if (states.Count == 0)
            return Result.Success();
        var onlineIds = states.Select(s => s.DeviceId).ToList();
        var aggregates = await db.Set<Device>().Where(d => onlineIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var state in states)
        {
            state.SetConnection(ConnectionState.Offline, now, policy.UnlicensedGrace);
            if (aggregates.TryGetValue(state.DeviceId, out var device))
                device.WentOffline(devices.First(d => d.DeviceId == state.DeviceId).Reason, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await live.DeviceStateChangedAsync([.. states.Select(DeviceLive.Change)], cancellationToken);
        return Result.Success();
    }
}

/// <summary>Writes the last contact of connected devices (presence monitor, every 10 s) in one statement per batch.</summary>
[SystemOnly]
public sealed record TouchDevicesCommand(IReadOnlyList<Guid> DeviceIds) : ICommand;

internal sealed class TouchDevicesCommandValidator : AbstractValidator<TouchDevicesCommand>
{
    public TouchDevicesCommandValidator() => RuleFor(x => x.DeviceIds).NotNull();
}

internal sealed class TouchDevicesCommandHandler(IAppDbContext db, ITenantScopeSetter scope, TimeProvider clock) : ICommandHandler<TouchDevicesCommand>
{
    public const int BatchSize = 500;

    public async Task<Result> Handle(TouchDevicesCommand request, CancellationToken cancellationToken)
    {
        scope.RunAsSystem();
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var batch in request.DeviceIds.Chunk(BatchSize))
        {
            await db.Set<DeviceState>()
                .Where(s => batch.Contains(s.DeviceId) && s.Connection == ConnectionState.Online)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.LastSeenAt, now).SetProperty(s => s.UpdatedAt, now), cancellationToken);
        }

        return Result.Success();
    }
}

/// <summary>Three session replacements within 5 minutes: a possible cloned device (05 section 2, rule 3).</summary>
[AllowDevice]
public sealed record ReportDuplicateSessionsCommand(Guid DeviceId, Guid TenantId, int Replacements) : ICommand;

internal sealed class ReportDuplicateSessionsCommandValidator : AbstractValidator<ReportDuplicateSessionsCommand>
{
    public ReportDuplicateSessionsCommandValidator() => RuleFor(x => x.DeviceId).NotEmpty();
}

internal sealed class ReportDuplicateSessionsCommandHandler(IAppDbContext db, ITenantScopeSetter scope, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<ReportDuplicateSessionsCommand>
{
    public async Task<Result> Handle(ReportDuplicateSessionsCommand request, CancellationToken cancellationToken)
    {
        scope.RunAsTenant(request.TenantId);
        var device = await db.Set<Device>().SingleOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken);
        if (device is null)
            return Result.Success();
        device.SuspectClone(request.Replacements, clock.GetUtcNow());
        audit.Add("device.possible_clone", "Device", device.Id.ToString(), $"{request.Replacements} session replacements within 5 minutes", success: false);
        return Result.Success();
    }
}

internal static class DeviceLive
{
    public static DeviceStateChange Change(DeviceState s) => new(
        s.DeviceId, s.TenantId, s.LocationId, s.Connection.ToString(), s.Health.ToString(), s.LicenseState.ToString(), s.CpuPercent, s.RamPercent, s.DiskPercent,
        DeviceQueries.Utc(s.LastSeenAt));
}
