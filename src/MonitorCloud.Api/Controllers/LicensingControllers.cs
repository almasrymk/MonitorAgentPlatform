using MediatR;
using Microsoft.AspNetCore.Mvc;
using MonitorCloud.Application.Licensing;

namespace MonitorCloud.Api.Controllers;

[Route("api/v1/subscription")]
public sealed class SubscriptionController(ISender sender) : ApiControllerBase(sender)
{
    [HttpGet]
    [ProducesResponseType<SubscriptionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetSubscriptionQuery(), cancellationToken));
}

[Route("api/v1/platform")]
public sealed class PlatformLicensingController(ISender sender) : ApiControllerBase(sender)
{
    [HttpGet("plans")]
    [ProducesResponseType<IReadOnlyList<PlanDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Plans(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetPlansQuery(), cancellationToken));

    [HttpGet("licensing/status")]
    [ProducesResponseType<LicensingStatusDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetLicensingStatusQuery(), cancellationToken));

    [HttpPost("licensing/sync")]
    [ProducesResponseType<SyncOutcome>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Sync(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new RunLicensingSyncCommand(), cancellationToken));
}
