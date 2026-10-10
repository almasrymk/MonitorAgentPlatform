using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Abstractions.Realtime;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Configuration.Contracts;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Monitoring.Contracts;
using MonitorCloud.Domain.Configuration;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Configuration;

public sealed record DeviceConfigurationDto(Guid DeviceId, int Version, ConfigDocument Document, int AppliedVersion, DateTimeOffset? AppliedAt, int? RejectedVersion, string? Error, DateTimeOffset UpdatedAt)
{
    /// <summary>The <c>ETag</c>: the configuration version.</summary>
    public string ETag => Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Creates configurations and their versions; the default document comes from the tenant defaults.</summary>
internal sealed class ConfigurationService(IAppDbContext db, TimeProvider clock) : IConfigurationVersioning
{
    public async Task<DeviceConfiguration> EnsureAsync(Guid deviceId, Guid tenantId, CancellationToken ct)
    {
        var existing = db.Set<DeviceConfiguration>().Local.FirstOrDefault(c => c.DeviceId == deviceId)
            ?? await db.Set<DeviceConfiguration>().SingleOrDefaultAsync(c => c.DeviceId == deviceId, ct);
        if (existing is not null)
            return existing;
        var defaults = await db.Set<TenantConfigurationDefaults>().AsNoTracking().SingleOrDefaultAsync(d => d.TenantId == tenantId, ct);
        var configuration = DeviceConfiguration.Create(deviceId, tenantId, defaults?.DocumentJson ?? ConfigDocuments.Serialize(ConfigDocuments.Default), clock.GetUtcNow());
        db.Set<DeviceConfiguration>().Add(configuration);
        return configuration;
    }

    public async Task BumpAsync(Guid deviceId, Guid tenantId, Guid? userId, CancellationToken ct) =>
        (await EnsureAsync(deviceId, tenantId, ct)).Bump(userId, clock.GetUtcNow());

    public async Task<int> VersionAsync(Guid deviceId, CancellationToken ct) =>
        await db.Set<DeviceConfiguration>().AsNoTracking().Where(c => c.DeviceId == deviceId).Select(c => c.Version).SingleOrDefaultAsync(ct);
}

internal sealed class AgentConfigurationReader(IReadDbContext db, IMonitorPointCatalog points) : IAgentConfigurationReader
{
    public async Task<AgentConfiguration?> GetAsync(Guid deviceId, CancellationToken ct)
    {
        var configuration = await db.Query<DeviceConfiguration>().SingleOrDefaultAsync(c => c.DeviceId == deviceId, ct);
        if (configuration is null)
            return null;
        var document = ConfigDocuments.TryParse(configuration.DocumentJson) ?? ConfigDocuments.Default;
        var definitions = await points.GetAsync(deviceId, ct);
        var agent = new AgentConfigDocument(
            configuration.Version, document.Telemetry, document.Thresholds,
            definitions.Select(p => new AgentPointDocument(p.Key, p.DisplayName, p.Type, p.Target, p.IntervalSeconds, p.AlertLevel, p.Enabled, p.ShowInShortcut,
                p.SettingsJson is null ? null : JsonDocument.Parse(p.SettingsJson).RootElement.Clone(), p.Type == "Database" ? p.Key : null)).ToList(),
            document.Features);
        return new AgentConfiguration(configuration.Version, ConfigDocuments.Serialize(agent));
    }
}

/// <summary>Every new device gets a configuration built from the tenant defaults (02 section 12).</summary>
internal sealed class CreateDefaultConfiguration(ConfigurationService configurations) : IIntegrationEventHandler<DeviceEnrolledV1>
{
    public async Task HandleAsync(DeviceEnrolledV1 integrationEvent, CancellationToken cancellationToken) =>
        await configurations.EnsureAsync(integrationEvent.DeviceId, integrationEvent.TenantId, cancellationToken);
}

[RequirePermission(Permissions.DevicesRead)]
public sealed record GetDeviceConfigurationQuery(Guid DeviceId) : IQuery<DeviceConfigurationDto>;

internal sealed class GetDeviceConfigurationQueryValidator : AbstractValidator<GetDeviceConfigurationQuery>
{
    public GetDeviceConfigurationQueryValidator() => RuleFor(x => x.DeviceId).NotEmpty();
}

internal sealed class GetDeviceConfigurationQueryHandler(IReadDbContext db, IDeviceDirectory devices) : IQueryHandler<GetDeviceConfigurationQuery, DeviceConfigurationDto>
{
    public async Task<Result<DeviceConfigurationDto>> Handle(GetDeviceConfigurationQuery request, CancellationToken cancellationToken)
    {
        if (await devices.FindAsync(request.DeviceId, cancellationToken) is null)
            return Error.NotFound(ErrorCodes.DeviceNotFound, "Device not found.");
        var configuration = await db.Query<DeviceConfiguration>().SingleOrDefaultAsync(c => c.DeviceId == request.DeviceId, cancellationToken);
        var ack = await db.Query<DeviceConfigurationAck>().SingleOrDefaultAsync(a => a.DeviceId == request.DeviceId, cancellationToken);
        return configuration is null
            ? new DeviceConfigurationDto(request.DeviceId, 0, ConfigDocuments.Default, ack?.AppliedVersion ?? 0, ack?.AppliedAt, ack?.RejectedVersion, ack?.Error, DateTimeOffset.MinValue)
            : new DeviceConfigurationDto(request.DeviceId, configuration.Version, ConfigDocuments.TryParse(configuration.DocumentJson) ?? ConfigDocuments.Default, ack?.AppliedVersion ?? 0,
                ack?.AppliedVersion > 0 ? ack.AppliedAt : null, ack?.RejectedVersion, ack?.Error, configuration.UpdatedAt);
    }
}

/// <summary>New thresholds for one device; <c>If-Match</c> carries the version the user edited (409 when it moved on).</summary>
[RequirePermission(Permissions.DevicesConfigure)]
public sealed record UpdateDeviceConfigurationCommand(Guid DeviceId, ConfigDocument Document, string? IfMatch) : ICommand<DeviceConfigurationDto>;

internal sealed class UpdateDeviceConfigurationCommandValidator : AbstractValidator<UpdateDeviceConfigurationCommand>
{
    public UpdateDeviceConfigurationCommandValidator() => RuleFor(x => x.DeviceId).NotEmpty();
}

internal sealed class UpdateDeviceConfigurationCommandHandler(
    IAppDbContext db, IDeviceDirectory devices, ConfigurationService configurations, ICurrentUser user, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<UpdateDeviceConfigurationCommand, DeviceConfigurationDto>
{
    public async Task<Result<DeviceConfigurationDto>> Handle(UpdateDeviceConfigurationCommand request, CancellationToken cancellationToken)
    {
        var device = await devices.FindAsync(request.DeviceId, cancellationToken);
        if (device is null)
            return Error.NotFound(ErrorCodes.DeviceNotFound, "Device not found.");
        var errors = ConfigDocuments.Validate(request.Document);
        if (errors.Count > 0)
            return CommonErrors.ValidationFailed(errors);
        // Version 0 = the defaults shown before the device has a configuration of its own.
        var current = await configurations.VersionAsync(device.Id, cancellationToken);
        if (request.IfMatch is { } expected && expected.Trim('"') != current.ToString(System.Globalization.CultureInfo.InvariantCulture))
            return CommonErrors.ConcurrencyConflict;
        var configuration = await configurations.EnsureAsync(device.Id, device.TenantId, cancellationToken);
        configuration.Update(ConfigDocuments.Serialize(request.Document), user.UserId, clock.GetUtcNow());
        audit.Add("device.configuration_updated", "Device", device.Id.ToString(), $"{device.Name}: version {configuration.Version}");
        var ack = await db.Set<DeviceConfigurationAck>().AsNoTracking().SingleOrDefaultAsync(a => a.DeviceId == device.Id, cancellationToken);
        return new DeviceConfigurationDto(device.Id, configuration.Version, request.Document, ack?.AppliedVersion ?? 0, ack?.AppliedVersion > 0 ? ack.AppliedAt : null, ack?.RejectedVersion,
            ack?.Error, configuration.UpdatedAt);
    }
}

[RequirePermission(Permissions.SettingsManage)]
public sealed record GetMonitoringDefaultsQuery : IQuery<ConfigDocument>;

internal sealed class GetMonitoringDefaultsQueryHandler(IReadDbContext db, ITenantContext scope) : IQueryHandler<GetMonitoringDefaultsQuery, ConfigDocument>
{
    public async Task<Result<ConfigDocument>> Handle(GetMonitoringDefaultsQuery request, CancellationToken cancellationToken)
    {
        var tenantId = scope.TenantId!.Value;
        var defaults = await db.Query<TenantConfigurationDefaults>().SingleOrDefaultAsync(d => d.TenantId == tenantId, cancellationToken);
        return ConfigDocuments.TryParse(defaults?.DocumentJson) ?? ConfigDocuments.Default;
    }
}

/// <summary>Settings > Monitoring: the thresholds new devices start with (existing devices keep theirs).</summary>
[RequirePermission(Permissions.SettingsManage)]
public sealed record UpdateMonitoringDefaultsCommand(ConfigDocument Document) : ICommand<ConfigDocument>;

internal sealed class UpdateMonitoringDefaultsCommandValidator : AbstractValidator<UpdateMonitoringDefaultsCommand>
{
    public UpdateMonitoringDefaultsCommandValidator() => RuleFor(x => x.Document).NotNull();
}

internal sealed class UpdateMonitoringDefaultsCommandHandler(IAppDbContext db, ITenantContext scope, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<UpdateMonitoringDefaultsCommand, ConfigDocument>
{
    public async Task<Result<ConfigDocument>> Handle(UpdateMonitoringDefaultsCommand request, CancellationToken cancellationToken)
    {
        var errors = ConfigDocuments.Validate(request.Document);
        if (errors.Count > 0)
            return CommonErrors.ValidationFailed(errors);
        var tenantId = scope.TenantId!.Value;
        var json = ConfigDocuments.Serialize(request.Document);
        var defaults = await db.Set<TenantConfigurationDefaults>().SingleOrDefaultAsync(d => d.TenantId == tenantId, cancellationToken);
        if (defaults is null)
            db.Set<TenantConfigurationDefaults>().Add(TenantConfigurationDefaults.Create(tenantId, json, clock.GetUtcNow()));
        else
            defaults.Update(json, clock.GetUtcNow());
        audit.Add("settings.monitoring_updated", "TenantConfigurationDefaults", tenantId.ToString());
        return request.Document;
    }
}

/// <summary>The device's answer to <c>ConfigUpdate</c> (05 section 3).</summary>
[AllowDevice]
public sealed record RecordConfigAppliedCommand(Guid DeviceId, Guid TenantId, int Version, bool Success, string? Error) : ICommand;

internal sealed class RecordConfigAppliedCommandValidator : AbstractValidator<RecordConfigAppliedCommand>
{
    public RecordConfigAppliedCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.Version).GreaterThanOrEqualTo(0);
    }
}

internal sealed class RecordConfigAppliedCommandHandler(IAppDbContext db, ITenantScopeSetter scope, IDeviceDirectory devices, TimeProvider clock) : ICommandHandler<RecordConfigAppliedCommand>
{
    public async Task<Result> Handle(RecordConfigAppliedCommand request, CancellationToken cancellationToken)
    {
        scope.RunAsTenant(request.TenantId);
        if (await devices.FindAsync(request.DeviceId, cancellationToken) is null)
            return Error.NotFound(ErrorCodes.DeviceNotFound, "Device not found.");
        var ack = await db.Set<DeviceConfigurationAck>().SingleOrDefaultAsync(a => a.DeviceId == request.DeviceId, cancellationToken);
        if (ack is null)
        {
            ack = DeviceConfigurationAck.Create(request.DeviceId, request.TenantId);
            db.Set<DeviceConfigurationAck>().Add(ack);
        }

        if (request.Success)
            ack.Applied(request.Version, clock.GetUtcNow());
        else
            ack.Rejected(request.Version, request.Error, clock.GetUtcNow());
        return Result.Success();
    }
}

/// <summary>The <c>ConfigUpdate</c> document for the gateway (on <c>Welcome</c> and after a change).</summary>
[AllowDevice]
public sealed record GetAgentConfigurationQuery(Guid DeviceId, Guid TenantId) : IQuery<AgentConfiguration?>;

internal sealed class GetAgentConfigurationQueryHandler(ITenantScopeSetter scope, IAgentConfigurationReader reader) : IQueryHandler<GetAgentConfigurationQuery, AgentConfiguration?>
{
    public async Task<Result<AgentConfiguration?>> Handle(GetAgentConfigurationQuery request, CancellationToken cancellationToken)
    {
        scope.RunAsTenant(request.TenantId);
        return Result.Success(await reader.GetAsync(request.DeviceId, cancellationToken));
    }
}

/// <summary><c>configApplied</c> to the device group (06 section 5).</summary>
internal sealed class PushConfigApplied(ILiveNotifier live) : IIntegrationEventHandler<DeviceConfigurationAppliedV1>
{
    public Task HandleAsync(DeviceConfigurationAppliedV1 integrationEvent, CancellationToken cancellationToken) =>
        live.ConfigAppliedAsync(new ConfigAppliedChange(integrationEvent.DeviceId, integrationEvent.TenantId, integrationEvent.Version, integrationEvent.Success, integrationEvent.Error), cancellationToken);
}

internal sealed class RemoteActionsSetting(IReadDbContext db) : IRemoteActionsSetting
{
    public async Task<bool> EnabledAsync(Guid deviceId, CancellationToken ct)
    {
        var json = await db.Query<DeviceConfiguration>().Where(c => c.DeviceId == deviceId).Select(c => c.DocumentJson).SingleOrDefaultAsync(ct)
            ?? await db.Query<TenantConfigurationDefaults>().Select(d => d.DocumentJson).FirstOrDefaultAsync(ct);
        return ConfigDocuments.TryParse(json)?.Features.RemoteActions ?? false;
    }
}