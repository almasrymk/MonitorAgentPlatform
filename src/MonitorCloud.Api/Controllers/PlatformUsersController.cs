using MediatR;
using Microsoft.AspNetCore.Mvc;
using MonitorCloud.Application.Audit;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Identity;
using MonitorCloud.Application.Identity.Users;

namespace MonitorCloud.Api.Controllers;

[Route("api/v1/platform")]
public sealed class PlatformUsersController(ISender sender) : ApiControllerBase(sender)
{
    public sealed record CreatePlatformUserRequest(string? FullName, string? Email, string? Role, string? Password);

    public sealed record UpdatePlatformUserRequest(string? FullName, string? Role);

    public sealed record ResetPasswordRequest(string? NewPassword);

    [HttpGet("users")]
    [ListEndpoint]
    [ProducesResponseType<PagedResult<UserListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? role, [FromQuery] string? status, [FromQuery] string? search, [FromQuery] string? sort, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetPlatformUsersQuery(role, status, search, sort, page, pageSize), cancellationToken));

    [HttpPost("users")]
    [ProducesResponseType<UserListItemDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreatePlatformUserRequest request, CancellationToken cancellationToken) =>
        Created(await Sender.Send(new CreatePlatformUserCommand(request.FullName ?? string.Empty, request.Email ?? string.Empty, request.Role ?? string.Empty, request.Password ?? string.Empty), cancellationToken));

    [HttpPut("users/{id:guid}")]
    [ProducesResponseType<UserListItemDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, UpdatePlatformUserRequest request, CancellationToken cancellationToken) =>
        FromVersioned(await Sender.Send(new UpdatePlatformUserCommand(id, request.FullName ?? string.Empty, request.Role ?? string.Empty, IfMatch), cancellationToken), u => u.Version);

    [HttpPost("users/{id:guid}/activate")]
    [ProducesResponseType<UserListItemDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SetPlatformUserActiveCommand(id, true), cancellationToken));

    [HttpPost("users/{id:guid}/deactivate")]
    [ProducesResponseType<UserListItemDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SetPlatformUserActiveCommand(id, false), cancellationToken));

    [HttpPost("users/{id:guid}/reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ResetPlatformUserPasswordCommand(id, request.NewPassword ?? string.Empty), cancellationToken));

    [HttpGet("audit")]
    [ListEndpoint]
    [ProducesResponseType<PagedResult<AuditRecordDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Audit([FromQuery] Guid? tenantId, [FromQuery] string? actor, [FromQuery] string? action, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? sort, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetPlatformAuditQuery(tenantId, actor, action, from, to, sort, page, pageSize), cancellationToken));
}
