using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Configuration.Contracts;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Application.Monitoring.Contracts;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Monitoring;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Monitoring;

internal sealed class MonitorPointCatalog(IReadDbContext db) : IMonitorPointCatalog
{
    public async Task<IReadOnlyList<MonitorPointDefinition>> GetAsync(Guid deviceId, CancellationToken ct) =>
        await db.Query<MonitorPoint>()
            .Where(p => p.DeviceId == deviceId)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Key)
            .Select(p => new MonitorPointDefinition(p.Key, p.DisplayName, p.Type, p.Target, p.IntervalSeconds, p.AlertLevel, p.Enabled, p.ShowInShortcut, p.SettingsJson))
            .ToListAsync(ct);
}

public static class MonitorPointErrors
{
    public static readonly Error NotFound = Error.NotFound("MONITOR_POINT_NOT_FOUND", "Monitor point not found.");
    public static readonly Error KeyTaken = Error.Conflict("MONITOR_POINT_KEY_TAKEN", "The device already has a monitor point with this key.");
}

public sealed record MonitorPointInput(
    string? Key, string DisplayName, string Type, string Target, int IntervalSeconds, string AlertLevel, bool Enabled, bool ShowInShortcut, JsonElement? Settings);

internal static class MonitorPointInputRules
{
    public static void Apply<T>(AbstractValidator<T> validator, Func<T, MonitorPointInput> input)
    {
        validator.RuleFor(x => input(x)).NotNull().WithName("point");
        validator.RuleFor(x => input(x).DisplayName).NotEmpty().MaximumLength(200).WithName("displayName");
        validator.RuleFor(x => input(x).Type).Must(t => MonitorPoint.Types.Contains(t)).WithName("type").WithMessage($"type must be one of {string.Join(", ", MonitorPoint.Types)}.");
        validator.RuleFor(x => input(x).Target).NotEmpty().MaximumLength(500).WithName("target");
        validator.RuleFor(x => input(x).IntervalSeconds).InclusiveBetween(5, 86_400).WithName("intervalSeconds");
        validator.RuleFor(x => input(x).AlertLevel).Must(a => MonitorPoint.AlertLevels.Contains(a)).WithName("alertLevel").WithMessage("alertLevel must be Problem, Warning or Unknown.");
        validator.RuleFor(x => input(x).Key).Matches("^[A-Za-z0-9_.:-]{1,64}$").When(x => input(x).Key is not null).WithName("key")
            .WithMessage("key: 1-64 letters, digits or - _ . :");
    }

    public static string? Settings(MonitorPointInput input) =>
        input.Settings is { ValueKind: JsonValueKind.Object } settings ? settings.GetRawText() : null;
}

/// <summary>Marks every agent-reported point of the device as managed by the cloud (05 section 8: the cloud version wins).</summary>
internal static class ManagedPoints
{
    public static async Task<List<MonitorPoint>> LoadAsync(IAppDbContext db, Guid deviceId, CancellationToken ct)
    {
        var points = await db.Set<MonitorPoint>().Where(p => p.DeviceId == deviceId).ToListAsync(ct);
        foreach (var p in points)
            p.Manage();
        return points;
    }
}

[RequirePermission(Permissions.MonitorPointsManage)]
[RequiresFeature(Features.MonitorPoints)]
public sealed record CreateMonitorPointCommand(Guid DeviceId, MonitorPointInput Point) : ICommand<MonitorPointDto>;

internal sealed class CreateMonitorPointCommandValidator : AbstractValidator<CreateMonitorPointCommand>
{
    public CreateMonitorPointCommandValidator() => MonitorPointInputRules.Apply(this, x => x.Point);
}

internal sealed class CreateMonitorPointCommandHandler(IAppDbContext db, IDeviceDirectory devices, IConfigurationVersioning versions, ICurrentUser user, IAuditLogger audit)
    : ICommandHandler<CreateMonitorPointCommand, MonitorPointDto>
{
    public async Task<Result<MonitorPointDto>> Handle(CreateMonitorPointCommand request, CancellationToken cancellationToken)
    {
        var device = await devices.FindAsync(request.DeviceId, cancellationToken);
        if (device is null)
            return IssueRules.DeviceNotFound;
        var points = await ManagedPoints.LoadAsync(db, device.Id, cancellationToken);
        var input = request.Point;
        var key = string.IsNullOrWhiteSpace(input.Key) ? $"cloud-{Guid.CreateVersion7():N}"[..18] : input.Key.Trim();
        if (points.Any(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)))
            return MonitorPointErrors.KeyTaken;
        var point = MonitorPoint.FromCloud(device.TenantId, device.Id, key, input.DisplayName, input.Type, input.Target, input.IntervalSeconds, input.AlertLevel, input.Enabled,
            input.ShowInShortcut, MonitorPointInputRules.Settings(input), points.Count == 0 ? 0 : points.Max(p => p.SortOrder) + 1);
        db.Set<MonitorPoint>().Add(point);
        await versions.BumpAsync(device.Id, device.TenantId, user.UserId, cancellationToken);
        audit.Add("monitorpoint.created", "MonitorPoint", point.Id.ToString(), $"{device.Name}: {point.DisplayName} ({point.Type})");
        return MonitorPointDtos.From(point, null);
    }
}

[RequirePermission(Permissions.MonitorPointsManage)]
[RequiresFeature(Features.MonitorPoints)]
public sealed record UpdateMonitorPointCommand(Guid DeviceId, Guid PointId, MonitorPointInput Point, string? Version) : ICommand<MonitorPointDto>;

internal sealed class UpdateMonitorPointCommandValidator : AbstractValidator<UpdateMonitorPointCommand>
{
    public UpdateMonitorPointCommandValidator() => MonitorPointInputRules.Apply(this, x => x.Point);
}

internal sealed class UpdateMonitorPointCommandHandler(IAppDbContext db, IDeviceDirectory devices, IConfigurationVersioning versions, ICurrentUser user, IAuditLogger audit)
    : ICommandHandler<UpdateMonitorPointCommand, MonitorPointDto>
{
    public async Task<Result<MonitorPointDto>> Handle(UpdateMonitorPointCommand request, CancellationToken cancellationToken)
    {
        var device = await devices.FindAsync(request.DeviceId, cancellationToken);
        if (device is null)
            return IssueRules.DeviceNotFound;
        var points = await ManagedPoints.LoadAsync(db, device.Id, cancellationToken);
        var point = points.SingleOrDefault(p => p.Id == request.PointId);
        if (point is null)
            return MonitorPointErrors.NotFound;
        db.ExpectVersion(point, Common.Versioning.Decode(request.Version));
        var input = request.Point;
        point.Edit(input.DisplayName, input.Type, input.Target, input.IntervalSeconds, input.AlertLevel, input.Enabled, input.ShowInShortcut, MonitorPointInputRules.Settings(input));
        await versions.BumpAsync(device.Id, device.TenantId, user.UserId, cancellationToken);
        audit.Add("monitorpoint.updated", "MonitorPoint", point.Id.ToString(), $"{device.Name}: {point.DisplayName}");
        return MonitorPointDtos.From(point, null);
    }
}

[RequirePermission(Permissions.MonitorPointsManage)]
[RequiresFeature(Features.MonitorPoints)]
public sealed record DeleteMonitorPointCommand(Guid DeviceId, Guid PointId) : ICommand;

internal sealed class DeleteMonitorPointCommandValidator : AbstractValidator<DeleteMonitorPointCommand>
{
    public DeleteMonitorPointCommandValidator() => RuleFor(x => x.PointId).NotEmpty();
}

internal sealed class DeleteMonitorPointCommandHandler(IAppDbContext db, IDeviceDirectory devices, IConfigurationVersioning versions, ICurrentUser user, IAuditLogger audit)
    : ICommandHandler<DeleteMonitorPointCommand>
{
    public async Task<Result> Handle(DeleteMonitorPointCommand request, CancellationToken cancellationToken)
    {
        var device = await devices.FindAsync(request.DeviceId, cancellationToken);
        if (device is null)
            return IssueRules.DeviceNotFound;
        var points = await ManagedPoints.LoadAsync(db, device.Id, cancellationToken);
        var point = points.SingleOrDefault(p => p.Id == request.PointId);
        if (point is null)
            return MonitorPointErrors.NotFound;
        var state = await db.Set<MonitorPointState>().SingleOrDefaultAsync(s => s.MonitorPointId == point.Id, cancellationToken);
        if (state is not null)
            db.Set<MonitorPointState>().Remove(state);
        db.Set<MonitorPoint>().Remove(point);
        await versions.BumpAsync(device.Id, device.TenantId, user.UserId, cancellationToken);
        audit.Add("monitorpoint.deleted", "MonitorPoint", point.Id.ToString(), $"{device.Name}: {point.DisplayName}");
        return Result.Success();
    }
}

internal static class MonitorPointDtos
{
    public static MonitorPointDto From(MonitorPoint p, MonitorPointState? s) => new(
        p.Id, p.Key, p.DisplayName, p.Type, p.Target, p.Enabled, p.ShowInShortcut, p.IntervalSeconds, (s?.Status ?? PointStatus.Unknown).ToString(), s?.Message ?? string.Empty,
        s?.ResponseMs, s?.LastCheckedAt, s?.StatusSince, p.AlertLevel, p.Origin, p.SettingsJson is null ? null : JsonDocument.Parse(p.SettingsJson).RootElement.Clone(),
        Common.Versioning.Encode(p.RowVersion));
}
