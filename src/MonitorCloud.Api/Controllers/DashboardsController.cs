using MediatR;
using Microsoft.AspNetCore.Mvc;
using MonitorCloud.Application.Tenancy;
using MonitorCloud.Application.Tenancy.Dashboards;

namespace MonitorCloud.Api.Controllers;

[Route("api/v1")]
public sealed class DashboardsController(ISender sender) : ApiControllerBase(sender)
{
    public sealed record EnrollmentCodeRequest(int? ExpiresInHours, int? MaxUses);

    [HttpGet("dashboard")]
    [ProducesResponseType<TenantDashboardDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Tenant([FromQuery] int? trendDays, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetTenantDashboardQuery(trendDays), cancellationToken));

    [HttpGet("locations/{id:guid}/dashboard")]
    [ProducesResponseType<LocationDashboardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Location(Guid id, [FromQuery] int? trendDays, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetLocationDashboardQuery(id, trendDays), cancellationToken));

    [HttpGet("platform/dashboard")]
    [ProducesResponseType<PlatformDashboardDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Platform([FromQuery] int? trendDays, [FromQuery] int? severityDays, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetPlatformDashboardQuery(trendDays, severityDays), cancellationToken));

    [HttpPost("locations/{id:guid}/enrollment-codes")]
    [ProducesResponseType<EnrollmentCodeCreatedDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> CreateEnrollmentCode(Guid id, EnrollmentCodeRequest r, CancellationToken cancellationToken) =>
        Created(await Sender.Send(new CreateEnrollmentCodeCommand(id, r.ExpiresInHours ?? 24, r.MaxUses), cancellationToken));

    [HttpGet("locations/{id:guid}/enrollment-codes")]
    [ProducesResponseType<IReadOnlyList<EnrollmentCodeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> EnrollmentCodes(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetEnrollmentCodesQuery(id), cancellationToken));

    [HttpDelete("locations/{id:guid}/enrollment-codes/{codeId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> RevokeEnrollmentCode(Guid id, Guid codeId, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new RevokeEnrollmentCodeCommand(id, codeId), cancellationToken));
}
