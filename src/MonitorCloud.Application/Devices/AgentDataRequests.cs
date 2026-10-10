using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Abstractions.Serialization;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Devices;

internal sealed class DeviceDirectory(IReadDbContext db) : IDeviceDirectory
{
    public async Task<DeviceRef?> FindAsync(Guid deviceId, CancellationToken cancellationToken) =>
        await db.Query<Device>()
            .Where(d => d.Id == deviceId && d.Status == DeviceStatus.Active)
            .Select(d => new DeviceRef(d.Id, d.TenantId, d.LocationId, d.Name,
                db.Query<DeviceState>().Where(s => s.DeviceId == d.Id).Select(s => s.LicenseState).FirstOrDefault() == LicenseStateValue.Licensed))
            .SingleOrDefaultAsync(cancellationToken);
}

/// <summary>An <c>InventoryUpdate</c> from the agent: stored when its hash changed (02 section 4).</summary>
[AllowDevice]
public sealed record UpsertInventoryCommand(Guid DeviceId, Guid TenantId, InventoryKind Kind, string Hash, byte[] Json) : ICommand<bool>;

internal sealed class UpsertInventoryCommandValidator : AbstractValidator<UpsertInventoryCommand>
{
    public UpsertInventoryCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.Hash).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Json).NotNull().Must(j => j.Length <= 4 * 1024 * 1024).WithMessage("An inventory document may have at most 4 MB.");
    }
}

internal sealed class UpsertInventoryCommandHandler(IAppDbContext db, ITenantScopeSetter scope, TimeProvider clock) : ICommandHandler<UpsertInventoryCommand, bool>
{
    public async Task<Result<bool>> Handle(UpsertInventoryCommand request, CancellationToken cancellationToken)
    {
        scope.RunAsTenant(request.TenantId);
        if (!await db.Set<Device>().AnyAsync(d => d.Id == request.DeviceId, cancellationToken))
            return DeviceErrors.NotFound;
        var now = clock.GetUtcNow();
        var existing = await db.Set<InventoryDocument>().SingleOrDefaultAsync(d => d.DeviceId == request.DeviceId && d.Kind == request.Kind, cancellationToken);
        if (existing is null)
        {
            db.Set<InventoryDocument>().Add(InventoryDocument.Create(request.DeviceId, request.TenantId, request.Kind, request.Json, request.Hash, now));
            return true;
        }

        return existing.Replace(request.Json, request.Hash, now);
    }
}

/// <summary>The device screen is open: keep live mode on for 60 s (05 section 4, <c>POST /devices/{id}/live-sessions</c>).</summary>
[RequirePermission(Permissions.DevicesRead)]
public sealed record StartLiveSessionCommand(Guid DeviceId) : ICommand<bool>;

internal sealed class StartLiveSessionCommandValidator : AbstractValidator<StartLiveSessionCommand>
{
    public StartLiveSessionCommandValidator() => RuleFor(x => x.DeviceId).NotEmpty();
}

internal sealed class StartLiveSessionCommandHandler(IDeviceDirectory devices, ILiveModeControl live) : ICommandHandler<StartLiveSessionCommand, bool>
{
    public async Task<Result<bool>> Handle(StartLiveSessionCommand request, CancellationToken cancellationToken)
    {
        if (await devices.FindAsync(request.DeviceId, cancellationToken) is null)
            return DeviceErrors.NotFound;
        return await live.RequestLiveAsync(request.DeviceId, cancellationToken);
    }
}

public sealed record InventoryDto(string Kind, DateTimeOffset UpdatedAt, JsonElement Document);

[RequirePermission(Permissions.DevicesRead)]
public sealed record GetDeviceInventoryQuery(Guid DeviceId, string Kind) : IQuery<InventoryDto>;

internal sealed class GetDeviceInventoryQueryValidator : AbstractValidator<GetDeviceInventoryQuery>
{
    public GetDeviceInventoryQueryValidator() =>
        RuleFor(x => x.Kind).Must(k => Enum.TryParse<InventoryKind>(k, true, out _)).WithMessage("kind must be one of hardware, os, network, disks, programs, services, users, sensors.");
}

internal sealed class GetDeviceInventoryQueryHandler(IReadDbContext db, IDeviceDirectory devices, IInventoryCodec codec) : IQueryHandler<GetDeviceInventoryQuery, InventoryDto>
{
    public static readonly Error NotReported = Error.NotFound("INVENTORY_NOT_FOUND", "The device has not reported this inventory yet.");

    public async Task<Result<InventoryDto>> Handle(GetDeviceInventoryQuery request, CancellationToken cancellationToken)
    {
        if (await devices.FindAsync(request.DeviceId, cancellationToken) is null)
            return DeviceErrors.NotFound;
        var kind = Enum.Parse<InventoryKind>(request.Kind, true);
        var document = await db.Query<InventoryDocument>()
            .Where(d => d.DeviceId == request.DeviceId && d.Kind == kind)
            .Select(d => new { d.Json, d.UpdatedAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (document is null)
            return NotReported;
        using var json = JsonDocument.Parse(codec.Decode(document.Json));
        return new InventoryDto(kind.ToString(), document.UpdatedAt, json.RootElement.Clone());
    }
}
