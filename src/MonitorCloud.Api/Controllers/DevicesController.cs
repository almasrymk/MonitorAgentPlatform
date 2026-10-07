using MediatR;
using Microsoft.AspNetCore.Mvc;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Devices;

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
}
