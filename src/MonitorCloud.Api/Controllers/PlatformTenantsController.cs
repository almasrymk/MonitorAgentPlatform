using MediatR;
using Microsoft.AspNetCore.Mvc;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Tenancy;

namespace MonitorCloud.Api.Controllers;

[Route("api/v1/platform/tenants")]
public sealed class PlatformTenantsController(ISender sender) : ApiControllerBase(sender)
{
    public sealed record CreateTenantRequest(string? Name, string? Code, string? Country, string? City, string? TimeZone, Guid? LicensingCustomerId);

    public sealed record UpdateTenantRequest(string? Name, string? Country, string? City, string? TimeZone);

    public sealed record ReasonRequest(string? Reason);

    [HttpGet]
    [ListEndpoint]
    [ProducesResponseType<PagedResult<TenantCardDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search, [FromQuery] string? plan, [FromQuery] string? health, [FromQuery] string? subscriptionStatus,
        [FromQuery] string? status, [FromQuery] string? sort, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetTenantsQuery(search, plan, health, subscriptionStatus, status, sort, page, pageSize), cancellationToken));

    [HttpGet("summary")]
    [ProducesResponseType<TenantsSummaryDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetTenantsSummaryQuery(), cancellationToken));

    [HttpPost]
    [ProducesResponseType<TenantDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateTenantRequest request, CancellationToken cancellationToken) =>
        Created(await Sender.Send(new CreateTenantCommand(request.Name ?? string.Empty, request.Code, request.Country ?? string.Empty, request.City ?? string.Empty, request.TimeZone, request.LicensingCustomerId), cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<TenantDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        FromVersioned(await Sender.Send(new GetTenantQuery(id), cancellationToken), t => t.Version);

    [HttpPut("{id:guid}")]
    [ProducesResponseType<TenantDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Update(Guid id, UpdateTenantRequest request, CancellationToken cancellationToken) =>
        FromVersioned(await Sender.Send(new UpdateTenantCommand(id, request.Name ?? string.Empty, request.Country ?? string.Empty, request.City ?? string.Empty, request.TimeZone ?? string.Empty, IfMatch), cancellationToken), t => t.Version);

    [HttpPost("{id:guid}/suspend")]
    [ProducesResponseType<TenantDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Suspend(Guid id, ReasonRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ChangeTenantStatusCommand(id, TenantTransition.Suspend, request.Reason), cancellationToken));

    [HttpPost("{id:guid}/resume")]
    [ProducesResponseType<TenantDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Resume(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ChangeTenantStatusCommand(id, TenantTransition.Resume, null), cancellationToken));

    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType<TenantDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Archive(Guid id, ReasonRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ChangeTenantStatusCommand(id, TenantTransition.Archive, request.Reason), cancellationToken));

    [HttpPost("{id:guid}/workspace-sessions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> OpenWorkspace(Guid id, ReasonRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new OpenWorkspaceCommand(id, request.Reason ?? string.Empty), cancellationToken));

    [HttpDelete("{id:guid}/workspace-sessions/current")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CloseWorkspace(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new CloseWorkspaceCommand(id), cancellationToken));
}
