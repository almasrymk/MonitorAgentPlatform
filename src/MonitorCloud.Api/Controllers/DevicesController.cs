using MediatR;
using Microsoft.AspNetCore.Mvc;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Devices;
using MonitorCloud.Application.Telemetry;

namespace MonitorCloud.Api.Controllers;

[Route("api/v1/devices")]
public sealed class DevicesController(ISender sender) : ApiControllerBase(sender)
{
    public sealed record DeviceRequest(string? Name, Guid? LocationId);

    [HttpGet]
    [ListEndpoint]
    [ProducesResponseType<PagedResult<DeviceListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? locationId, [FromQuery] string? search, [FromQuery] string? os, [FromQuery] string? status, [FromQuery] string? license,
        [FromQuery] string? sort, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetDevicesQuery(locationId, search, os, status, license, sort, page, pageSize), cancellationToken));

    [HttpGet("summary")]
    [ProducesResponseType<DevicesSummaryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary([FromQuery] Guid? locationId, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetDevicesSummaryQuery(locationId), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DeviceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        FromVersioned(await Sender.Send(new GetDeviceQuery(id), cancellationToken), d => d.Version);

    [HttpPut("{id:guid}")]
    [ProducesResponseType<DeviceListItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Update(Guid id, DeviceRequest r, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateDeviceCommand(id, r.Name ?? string.Empty, r.LocationId ?? Guid.Empty, IfMatch), cancellationToken));

    [HttpPost("{id:guid}/retire")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Retire(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new RetireDeviceCommand(id), cancellationToken));

    [HttpPost("{id:guid}/unlicense")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Unlicense(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UnlicenseDeviceCommand(id), cancellationToken));

    [HttpGet("{id:guid}/overview")]
    [ProducesResponseType<DeviceOverviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Overview(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetDeviceOverviewQuery(id), cancellationToken));

    [HttpGet("{id:guid}/metrics")]
    [ProducesResponseType<DeviceMetricsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Metrics(Guid id, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? metrics, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetDeviceMetricsQuery(id, from, to, metrics), cancellationToken));

    [HttpGet("{id:guid}/disks")]
    [ProducesResponseType<IReadOnlyList<DiskDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Disks(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetDeviceDisksQuery(id), cancellationToken));

    [HttpGet("{id:guid}/inventory/{kind}")]
    [ProducesResponseType<InventoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Inventory(Guid id, string kind, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetDeviceInventoryQuery(id, kind), cancellationToken));

    /// <summary>Keeps live mode on for 60 s (05 section 4); 204 whether or not the device is connected.</summary>
    [HttpPost("{id:guid}/live-sessions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> LiveSession(Guid id, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new StartLiveSessionCommand(id), cancellationToken);
        return result.IsSuccess ? NoContent() : Problem(result.Error!);
    }
}