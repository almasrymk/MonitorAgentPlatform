using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MonitorCloud.Application.Identity;
using MonitorCloud.Application.Identity.Commands.AcceptInvitation;
using MonitorCloud.Application.Identity.Commands.ChangePassword;
using MonitorCloud.Application.Identity.Commands.Login;
using MonitorCloud.Application.Identity.Commands.Logout;
using MonitorCloud.Application.Identity.Commands.Refresh;
using MonitorCloud.Application.Identity.Commands.SetLanguage;
using MonitorCloud.Application.Identity.Queries.GetMe;

namespace MonitorCloud.Api.Controllers;

[Route("api/v1/auth")]
public sealed class AuthController(ISender sender) : ApiControllerBase(sender)
{
    public sealed record LoginRequest(string? Email, string? Password);

    public sealed record RefreshRequest(string? RefreshToken);

    public sealed record AcceptInvitationRequest(string? Token, string? Password);

    public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

    public sealed record LanguageRequest(string? Language);

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceCollectionExtensions.AuthRateLimit)]
    [ProducesResponseType<AuthResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status423Locked, "application/problem+json")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new LoginCommand(request.Email ?? string.Empty, request.Password ?? string.Empty), cancellationToken));

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceCollectionExtensions.RefreshRateLimit)]
    [ProducesResponseType<AuthResultDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new RefreshCommand(request.RefreshToken ?? string.Empty), cancellationToken));

    [HttpPost("logout")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceCollectionExtensions.RefreshRateLimit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new LogoutCommand(request.RefreshToken ?? string.Empty), cancellationToken));

    [HttpPost("invitations/accept")]
    [AllowAnonymous]
    [EnableRateLimiting(ApiServiceCollectionExtensions.AuthRateLimit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AcceptInvitation(AcceptInvitationRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new AcceptInvitationCommand(request.Token ?? string.Empty, request.Password ?? string.Empty), cancellationToken));

    [HttpGet("me")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetMeQuery(), cancellationToken));

    [HttpPut("me/password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ChangePasswordCommand(request.CurrentPassword ?? string.Empty, request.NewPassword ?? string.Empty), cancellationToken));

    [HttpPut("me/language")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetLanguage(LanguageRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SetLanguageCommand(request.Language ?? string.Empty), cancellationToken));
}
