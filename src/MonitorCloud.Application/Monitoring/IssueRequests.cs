using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Domain.Monitoring;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Monitoring;

public enum IssueAction
{
    Raised,
    SeverityChanged,
    Cleared,
}

public static class IssueRules
{
    /// <summary>Backlog issues older than this are recorded without a notification (05 section 2, rule 7).</summary>
    public static readonly TimeSpan NotifyWindow = TimeSpan.FromMinutes(15);

    /// <summary>Clock skew that raises the <c>clock-skew</c> alert (rule 8).</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public static readonly Error DeviceNotFound = Error.NotFound(ErrorCodes.DeviceNotFound, "Device not found.");
}

/// <summary>An <c>IssueEvent</c> of the agent (05 section 3): opens, touches or resolves the alert of the issue key.</summary>
[AllowDevice]
public sealed record ApplyIssueEventCommand(
    Guid DeviceId, Guid TenantId, string IssueKey, IssueAction Action, AlertSeverity Severity, string? Category, string? Title, string? Message, DateTimeOffset OccurredAt)
    : ICommand<bool>;

internal sealed class ApplyIssueEventCommandValidator : AbstractValidator<ApplyIssueEventCommand>
{
    public ApplyIssueEventCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.IssueKey).NotEmpty().MaximumLength(128)
            .Must(k => k is not (CloudIssues.DeviceOffline or CloudIssues.License or CloudIssues.ClockSkew or CloudIssues.CloneSuspected))
            .WithMessage("This issue key is reserved for the cloud.");
        RuleFor(x => x.Severity).IsInEnum();
        RuleFor(x => x.Action).IsInEnum();
    }
}

internal sealed class ApplyIssueEventCommandHandler(ITenantScopeSetter scope, IDeviceDirectory devices, AlertBook alerts, TimeProvider clock)
    : ICommandHandler<ApplyIssueEventCommand, bool>
{
    public async Task<Result<bool>> Handle(ApplyIssueEventCommand request, CancellationToken cancellationToken)
    {
        scope.RunAsTenant(request.TenantId);
        var device = await devices.FindAsync(request.DeviceId, cancellationToken);
        if (device is null)
            return IssueRules.DeviceNotFound;

        var now = clock.GetUtcNow();
        var at = request.OccurredAt == default || request.OccurredAt > now ? now : request.OccurredAt;
        if (request.Action == IssueAction.Cleared)
            return await alerts.ResolveAsync(device.Id, request.IssueKey, at, cancellationToken);

        var notify = now - at <= IssueRules.NotifyWindow;
        return await alerts.RaiseAsync(device.TenantId, device.LocationId, device.Id, request.IssueKey, request.Category, request.Severity,
            request.Title ?? request.IssueKey, request.Message, AlertSource.Agent, at, notify, cancellationToken);
    }
}

/// <summary>The gateway saw the agent's clock off by more than 5 minutes, or back in line (rule 8).</summary>
[AllowDevice]
public sealed record ReportClockSkewCommand(Guid DeviceId, Guid TenantId, bool Skewed, int SkewSeconds) : ICommand;

internal sealed class ReportClockSkewCommandValidator : AbstractValidator<ReportClockSkewCommand>
{
    public ReportClockSkewCommandValidator() => RuleFor(x => x.DeviceId).NotEmpty();
}

internal sealed class ReportClockSkewCommandHandler(ITenantScopeSetter scope, IDeviceDirectory devices, AlertBook alerts, TimeProvider clock) : ICommandHandler<ReportClockSkewCommand>
{
    public async Task<Result> Handle(ReportClockSkewCommand request, CancellationToken cancellationToken)
    {
        scope.RunAsTenant(request.TenantId);
        var device = await devices.FindAsync(request.DeviceId, cancellationToken);
        if (device is null)
            return IssueRules.DeviceNotFound;
        var now = clock.GetUtcNow();
        if (!request.Skewed)
        {
            await alerts.ResolveAsync(device.Id, CloudIssues.ClockSkew, now, cancellationToken);
            return Result.Success();
        }

        var minutes = Math.Round(Math.Abs(request.SkewSeconds) / 60.0, 1);
        await alerts.RaiseAsync(device.TenantId, device.LocationId, device.Id, CloudIssues.ClockSkew, AlertCategories.System, AlertSeverity.Warning,
            "Device clock is out of sync", $"The device clock differs from the server by {minutes} minutes.", AlertSource.Cloud, now, true, cancellationToken);
        return Result.Success();
    }
}

public sealed record MonitorPointReportItem(
    string Key, string? DisplayName, string? Type, string? Target, bool Enabled, PointStatus Status, string? Message, decimal? ResponseMs, DateTimeOffset? LastChecked,
    DateTimeOffset? StatusSince, int IntervalSeconds);

/// <summary>A <c>MonitorPointReport</c> (05 section 3): imports the agent's monitor points and their latest status.</summary>
[AllowDevice]
public sealed record ApplyMonitorPointReportCommand(Guid DeviceId, Guid TenantId, bool Full, IReadOnlyList<MonitorPointReportItem> Points) : ICommand<int>;

internal sealed class ApplyMonitorPointReportCommandValidator : AbstractValidator<ApplyMonitorPointReportCommand>
{
    public const int MaxPoints = 500;

    public ApplyMonitorPointReportCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.Points).NotNull().Must(p => p.Count <= MaxPoints).WithMessage($"A report may have at most {MaxPoints} points.");
        RuleForEach(x => x.Points).ChildRules(p => p.RuleFor(i => i.Key).NotEmpty().MaximumLength(64));
    }
}

internal sealed class ApplyMonitorPointReportCommandHandler(IAppDbContext db, ITenantScopeSetter scope, IDeviceDirectory devices)
    : ICommandHandler<ApplyMonitorPointReportCommand, int>
{
    public async Task<Result<int>> Handle(ApplyMonitorPointReportCommand request, CancellationToken cancellationToken)
    {
        scope.RunAsTenant(request.TenantId);
        var device = await devices.FindAsync(request.DeviceId, cancellationToken);
        if (device is null)
            return IssueRules.DeviceNotFound;

        var points = await db.Set<MonitorPoint>().Where(p => p.DeviceId == device.Id).ToDictionaryAsync(p => p.Key, StringComparer.Ordinal, cancellationToken);
        var ids = points.Values.Select(p => p.Id).ToList();
        var states = await db.Set<MonitorPointState>().Where(s => ids.Contains(s.MonitorPointId)).ToDictionaryAsync(s => s.MonitorPointId, cancellationToken);
        var order = points.Count;
        // Once the cloud manages the points (an edit in the portal, 05 section 8), the agent only reports their status.
        var managed = points.Values.Any(p => p.Origin == MonitorPoint.CloudOrigin);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in request.Points.DistinctBy(p => p.Key, StringComparer.Ordinal))
        {
            reported.Add(item.Key);
            if (!points.TryGetValue(item.Key, out var point))
            {
                if (managed)
                    continue;
                point = MonitorPoint.FromAgent(device.TenantId, device.Id, item.Key, item.DisplayName ?? item.Key, item.Type ?? "Custom", item.Target ?? string.Empty,
                    item.Enabled, item.IntervalSeconds, order++);
                db.Set<MonitorPoint>().Add(point);
                points[item.Key] = point;
            }
            else if (point.Origin != MonitorPoint.CloudOrigin)
            {
                point.UpdateFromAgent(item.DisplayName ?? point.DisplayName, item.Type ?? point.Type, item.Target ?? point.Target, item.Enabled, item.IntervalSeconds);
            }

            if (!states.TryGetValue(point.Id, out var state))
            {
                state = MonitorPointState.Create(point.Id, device.TenantId, device.Id);
                db.Set<MonitorPointState>().Add(state);
                states[point.Id] = state;
            }

            state.Update(item.Status, item.Message, item.ResponseMs, item.LastChecked, item.StatusSince);
        }

        if (request.Full && !managed)
        {
            foreach (var gone in points.Values.Where(p => p.Origin == "Agent" && !reported.Contains(p.Key)).ToList())
            {
                if (states.TryGetValue(gone.Id, out var state))
                    db.Set<MonitorPointState>().Remove(state);
                db.Set<MonitorPoint>().Remove(gone);
            }
        }

        return reported.Count;
    }
}
