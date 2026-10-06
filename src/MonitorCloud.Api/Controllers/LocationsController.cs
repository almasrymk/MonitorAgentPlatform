using MediatR;
using Microsoft.AspNetCore.Mvc;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Tenancy;

namespace MonitorCloud.Api.Controllers;

[Route("api/v1/locations")]
public sealed class LocationsController(ISender sender) : ApiControllerBase(sender)
{
    public sealed record LocationRequest(
        string? Name, string? Code, string? City, string? Country, string? AddressLine, string? TimeZone,
        string? ContactName, string? ContactEmail, string? ContactPhone);

    [HttpGet]
    [ListEndpoint]
    [ProducesResponseType<PagedResult<LocationCardDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? sort, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetLocationsQuery(search, sort, page, pageSize), cancellationToken));

    [HttpPost]
    [ProducesResponseType<LocationDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(LocationRequest r, CancellationToken cancellationToken) =>
        Created(await Sender.Send(new CreateLocationCommand(r.Name ?? string.Empty, r.Code ?? string.Empty, r.City, r.Country, r.AddressLine, r.TimeZone ?? string.Empty, r.ContactName, r.ContactEmail, r.ContactPhone), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<LocationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        FromVersioned(await Sender.Send(new GetLocationQuery(id), cancellationToken), l => l.Version);

    [HttpPut("{id:guid}")]
    [ProducesResponseType<LocationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Update(Guid id, LocationRequest r, CancellationToken cancellationToken) =>
        FromVersioned(await Sender.Send(new UpdateLocationCommand(id, r.Name ?? string.Empty, r.Code ?? string.Empty, r.City, r.Country, r.AddressLine, r.TimeZone ?? string.Empty, r.ContactName, r.ContactEmail, r.ContactPhone, IfMatch), cancellationToken), l => l.Version);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeleteLocationCommand(id), cancellationToken));
}
