using Google.Protobuf.WellKnownTypes;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.AgentGateway.Sessions;
using MonitorCloud.AgentProtocol.V1;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Application.Monitoring;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.Domain.Monitoring;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.AgentGateway.Handlers;

/// <summary><c>IssueEvent</c> (guaranteed) -> <see cref="ApplyIssueEventCommand"/>, acknowledged after the save.</summary>
public sealed class IssueHandler(IServiceScopeFactory scopes, TimeProvider clock) : IAgentMessageHandler
{
    public AgentMessage.BodyOneofCase Kind => AgentMessage.BodyOneofCase.Issue;

    public async Task HandleAsync(AgentSession session, AgentMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);
        if (!await GuaranteedMessages.AcceptAsync(session, message, clock, cancellationToken))
            return;
        var issue = message.Issue;
        if (!session.Restricted && !string.IsNullOrWhiteSpace(issue.IssueKey) && issue.Action != AgentProtocol.V1.IssueAction.Unspecified)
        {
            var action = issue.Action switch
            {
                AgentProtocol.V1.IssueAction.Raised => Application.Monitoring.IssueAction.Raised,
                AgentProtocol.V1.IssueAction.SeverityChanged => Application.Monitoring.IssueAction.SeverityChanged,
                _ => Application.Monitoring.IssueAction.Cleared,
            };
            var severity = issue.Severity switch
            {
                Severity.Critical => AlertSeverity.Critical,
                Severity.Warning => AlertSeverity.Warning,
                _ => AlertSeverity.Info,
            };
            var occurredAt = issue.OccurredAt?.ToDateTimeOffset() ?? clock.GetUtcNow();
            await using var scope = scopes.CreateAsyncScope();
            var applied = await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ApplyIssueEventCommand(
                session.DeviceId, session.TenantId, issue.IssueKey, action, severity, issue.Category, issue.Title, issue.Message, occurredAt), cancellationToken);
            if (applied.IsFailure && applied.Error!.Kind != ErrorKind.Validation)
                return;
        }

        await GuaranteedMessages.AckAsync(session, message, clock, cancellationToken);
    }
}

/// <summary><c>MonitorPointReport</c> (guaranteed) -> <see cref="ApplyMonitorPointReportCommand"/>.</summary>
public sealed class MonitorPointReportHandler(IServiceScopeFactory scopes, TimeProvider clock) : IAgentMessageHandler
{
    public AgentMessage.BodyOneofCase Kind => AgentMessage.BodyOneofCase.MonitorPoints;

    public async Task HandleAsync(AgentSession session, AgentMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);
        if (!await GuaranteedMessages.AcceptAsync(session, message, clock, cancellationToken))
            return;
        if (!session.Restricted)
        {
            var report = message.MonitorPoints;
            var points = report.Points.Select(p => new MonitorPointReportItem(
                p.Key, p.DisplayName, p.Type, p.Target, p.Enabled, Status(p.Status), p.Message, p.HasResponseMs && double.IsFinite(p.ResponseMs) ? (decimal)Math.Clamp(p.ResponseMs, 0, 9_999_999) : null,
                p.LastChecked?.ToDateTimeOffset(), p.StatusSince?.ToDateTimeOffset(), (int)Math.Min(p.IntervalSeconds, int.MaxValue))).ToList();
            await using var scope = scopes.CreateAsyncScope();
            var applied = await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(new ApplyMonitorPointReportCommand(session.DeviceId, session.TenantId, report.Full, points), cancellationToken);
            if (applied.IsFailure && applied.Error!.Kind != ErrorKind.Validation)
                return;
        }

        await GuaranteedMessages.AckAsync(session, message, clock, cancellationToken);
    }

    private static Domain.Monitoring.PointStatus Status(AgentProtocol.V1.PointStatus status) => status switch
    {
        AgentProtocol.V1.PointStatus.PointHealthy => Domain.Monitoring.PointStatus.Healthy,
        AgentProtocol.V1.PointStatus.PointWarning => Domain.Monitoring.PointStatus.Warning,
        AgentProtocol.V1.PointStatus.PointCritical => Domain.Monitoring.PointStatus.Critical,
        _ => Domain.Monitoring.PointStatus.Unknown,
    };
}

/// <summary>
/// Rule 8 of 05 section 2: the first message of a session and every change between "in line" and "more than 5 minutes
/// off" are reported, so the <c>clock-skew</c> alert opens and resolves itself.
/// </summary>
public sealed class ClockSkewMonitor(IServiceScopeFactory scopes, TimeProvider clock)
{
    public async Task CheckAsync(AgentSession session, AgentMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(message);
        if (message.SentAt is null)
            return;
        var skew = message.SentAt.ToDateTimeOffset() - clock.GetUtcNow();
        var skewed = skew.Duration() > IssueRules.MaxClockSkew;
        if (!session.TrySetClockSkew(skewed))
            return;
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new ReportClockSkewCommand(session.DeviceId, session.TenantId, skewed, (int)Math.Clamp(skew.TotalSeconds, int.MinValue, int.MaxValue)), cancellationToken);
    }
}

/// <summary>A licence change of a connected device is pushed at once as <c>LicenseUpdate</c> (gateway test 10).</summary>
public sealed class PushLicenseUpdates(IAgentSessionRegistry registry, IDeviceSeats seats, TimeProvider clock) : IIntegrationEventHandler<DeviceLicenseChangedV1>
{
    public async Task HandleAsync(DeviceLicenseChangedV1 integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (registry.Find(integrationEvent.DeviceId) is not { } session)
            return;
        var seat = await seats.FindAsync(integrationEvent.DeviceId, cancellationToken);
        if (seat is null)
            return;
        var now = clock.GetUtcNow();
        var update = new LicenseUpdate
        {
            State = seat.State == "Licensed" ? LicenseState.Licensed : LicenseState.Unlicensed,
            ReasonCode = seat.ReasonCode ?? string.Empty,
            Token = seat.Token ?? string.Empty,
            CheckAfter = Timestamp.FromDateTimeOffset(seat.CheckAfter),
        };
        await session.SendAsync(new CloudMessage { MessageId = Guid.CreateVersion7().ToString("N"), SentAt = Timestamp.FromDateTimeOffset(now), LicenseUpdate = update }, cancellationToken);
    }
}
