using MediatR;
using Microsoft.AspNetCore.Mvc;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Monitoring;
using MonitorCloud.Application.Notifications;
using MonitorCloud.Application.Tenancy;

namespace MonitorCloud.Api.Controllers;

[Route("api/v1")]
public sealed class AlertsController(ISender sender) : ApiControllerBase(sender)
{
    public sealed record MarkReadRequest(IReadOnlyList<Guid>? Ids, bool? All);

    [HttpGet("alerts")]
    [ListEndpoint]
    [ProducesResponseType<PagedResult<AlertDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? locationId, [FromQuery] Guid? deviceId, [FromQuery] string? severity, [FromQuery] string? status, [FromQuery] string? category,
        [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? sort, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetAlertsQuery(locationId, deviceId, severity, status, category, from, to, sort, page, pageSize), cancellationToken));

    [HttpGet("alerts/{id:guid}")]
    [ProducesResponseType<AlertDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetAlertQuery(id), cancellationToken));

    [HttpPost("alerts/{id:guid}/acknowledge")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new AcknowledgeAlertCommand(id), cancellationToken));

    [HttpPost("alerts/{id:guid}/resolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Resolve(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ResolveAlertCommand(id), cancellationToken));

    [HttpGet("devices/{id:guid}/alerts")]
    [ProducesResponseType<PagedResult<AlertDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> DeviceAlerts(Guid id, [FromQuery] string? status, [FromQuery] string? sort, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetDeviceAlertsQuery(id, status, sort, page, pageSize), cancellationToken));

    [HttpGet("devices/{id:guid}/monitor-points")]
    [ProducesResponseType<IReadOnlyList<MonitorPointDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> MonitorPoints(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetDeviceMonitorPointsQuery(id), cancellationToken));

    [HttpGet("notifications")]
    [ListEndpoint]
    [ProducesResponseType<PagedResult<NotificationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Notifications(
        [FromQuery] Guid? locationId, [FromQuery] string? severity, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? sort,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetNotificationsQuery(locationId, severity, from, to, sort, page, pageSize), cancellationToken));

    [HttpGet("notifications/unread-count")]
    [ProducesResponseType<UnreadCountDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UnreadCount(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetUnreadNotificationCountQuery(), cancellationToken));

    [HttpPost("notifications/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(MarkReadRequest r, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new MarkNotificationsReadCommand(r.Ids, r.All ?? false), cancellationToken));

    [HttpGet("platform/alerts")]
    [ListEndpoint]
    [ProducesResponseType<PagedResult<PlatformAlertDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PlatformAlerts(
        [FromQuery] string? severity, [FromQuery] Guid? tenantId, [FromQuery] string? status, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] string? sort, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetPlatformAlertsQuery(severity, tenantId, status, from, to, sort, page, pageSize), cancellationToken));

    [HttpGet("platform/notifications")]
    [ListEndpoint]
    [ProducesResponseType<PagedResult<NotificationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PlatformNotifications(
        [FromQuery] string? severity, [FromQuery] string? sort, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetPlatformNotificationsQuery(severity, sort, page, pageSize), cancellationToken));

    [HttpGet("platform/notifications/unread-count")]
    [ProducesResponseType<UnreadCountDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PlatformUnreadCount(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetPlatformUnreadCountQuery(), cancellationToken));

    [HttpPost("platform/notifications/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> PlatformMarkRead(MarkReadRequest r, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new MarkPlatformNotificationsReadCommand(r.Ids, r.All ?? false), cancellationToken));
}

[Route("api/v1/settings")]
public sealed class SettingsController(ISender sender) : ApiControllerBase(sender)
{
    public sealed record GeneralRequest(string? TimeZone, string? DefaultLanguage, string? OfflineAlertSeverity, int? OfflineAlertDelayMinutes);

    public sealed record AlertSettingsRequest(bool? EmailEnabled, bool? InAppEnabled, bool? WebhookEnabled, string? WebhookUrl);

    public sealed record RecipientRequest(string? Name, string? Email, string? Events, Guid? LocationId, bool? IsActive);

    [HttpGet("general")]
    [ProducesResponseType<GeneralSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> General(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetGeneralSettingsQuery(), cancellationToken));

    [HttpPut("general")]
    [ProducesResponseType<GeneralSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateGeneral(GeneralRequest r, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateGeneralSettingsCommand(r.TimeZone ?? string.Empty, r.DefaultLanguage ?? string.Empty, r.OfflineAlertSeverity ?? string.Empty,
            r.OfflineAlertDelayMinutes ?? 0), cancellationToken));

    [HttpGet("alerts")]
    [ProducesResponseType<AlertSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Alerts(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetAlertSettingsQuery(), cancellationToken));

    [HttpPut("alerts")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateAlerts(AlertSettingsRequest r, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateAlertSettingsCommand(r.EmailEnabled ?? true, r.InAppEnabled ?? true, r.WebhookEnabled ?? false, r.WebhookUrl), cancellationToken));

    [HttpGet("alerts/recipients")]
    [ProducesResponseType<IReadOnlyList<RecipientDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Recipients(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetRecipientsQuery(), cancellationToken));

    [HttpPost("alerts/recipients")]
    [ProducesResponseType<RecipientDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> AddRecipient(RecipientRequest r, CancellationToken cancellationToken) =>
        Created(await Sender.Send(new CreateRecipientCommand(r.Name ?? string.Empty, r.Email ?? string.Empty, r.Events ?? "All", r.LocationId), cancellationToken));

    [HttpPut("alerts/recipients/{id:guid}")]
    [ProducesResponseType<RecipientDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> UpdateRecipient(Guid id, RecipientRequest r, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateRecipientCommand(id, r.Name ?? string.Empty, r.Email ?? string.Empty, r.Events ?? "All", r.LocationId, r.IsActive ?? true), cancellationToken));

    [HttpDelete("alerts/recipients/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> DeleteRecipient(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeleteRecipientCommand(id), cancellationToken));
}
