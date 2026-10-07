using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MonitorCloud.Api.Authentication;
using MonitorCloud.Application.Devices.Enroll;
using MonitorCloud.Application.Devices.Token;

namespace MonitorCloud.Api.Controllers;

/// <summary>The HTTPS endpoints used by the agent (05 section 1). The stream itself is gRPC (M4).</summary>
[Route("api/agent/v1")]
public sealed class AgentController(ISender sender) : ApiControllerBase(sender)
{
    public sealed record EnrollRequest(
        string? ProductKey, string? Fingerprint, string? Hostname, string? OsFamily, string? OsName, string? OsVersion, string? Architecture,
        string? AgentVersion, int? ProtocolVersion, string? LocationCode, string? LocalIp, string? MacAddress);

    public sealed record TokenRequest(Guid? DeviceId, string? DeviceSecret);

    [HttpPost("enroll")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceCollectionExtensions.EnrollRateLimit)]
    [ProducesResponseType<EnrollmentResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Enroll(EnrollRequest r, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(
            new EnrollDeviceCommand(r.ProductKey ?? string.Empty, r.Fingerprint ?? string.Empty, r.Hostname ?? string.Empty, r.OsFamily, r.OsName, r.OsVersion,
                r.Architecture, r.AgentVersion, r.ProtocolVersion ?? 0, r.LocationCode, r.LocalIp, r.MacAddress),
            cancellationToken));

    [HttpPost("token")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceCollectionExtensions.AgentTokenRateLimit)]
    [ProducesResponseType<DeviceTokenResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    public async Task<IActionResult> Token(TokenRequest r, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new IssueDeviceTokenCommand(r.DeviceId ?? Guid.Empty, r.DeviceSecret ?? string.Empty), cancellationToken));

    [HttpPost("credential/rotate")]
    [Authorize(Policy = AuthenticationSetup.DeviceScheme)]
    [ProducesResponseType<RotatedSecret>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Rotate(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new RotateDeviceCredentialCommand(), cancellationToken));
}
