using MediatR;
using Microsoft.AspNetCore.Mvc;
using MonitorCloud.Application.Audit;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Identity;
using MonitorCloud.Application.Identity.Users;

namespace MonitorCloud.Api.Controllers;

[Route("api/v1")]
public sealed class UsersController(ISender sender) : ApiControllerBase(sender)
{
    public sealed record InviteUserRequest(string? FullName, string? Email, string? Role, IReadOnlyList<Guid>? LocationIds);

    public sealed record UpdateUserRequest(string? FullName, string? Role, IReadOnlyList<Guid>? LocationIds);

    [HttpGet("users")]
    [ListEndpoint]
    [ProducesResponseType<PagedResult<UserListItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? role, [FromQuery] string? status, [FromQuery] string? search, [FromQuery] string? sort, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetUsersQuery(role, status, search, sort, page, pageSize), cancellationToken));

    [HttpPost("users")]
    [ProducesResponseType<UserListItemDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Invite(InviteUserRequest request, CancellationToken cancellationToken) =>
        Created(await Sender.Send(new InviteUserCommand(request.FullName ?? string.Empty, request.Email ?? string.Empty, request.Role ?? string.Empty, request.LocationIds), cancellationToken));

    [HttpPut("users/{id:guid}")]
    [ProducesResponseType<UserListItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Update(Guid id, UpdateUserRequest request, CancellationToken cancellationToken) =>
        FromVersioned(await Sender.Send(new UpdateUserCommand(id, request.FullName ?? string.Empty, request.Role ?? string.Empty, request.LocationIds, IfMatch), cancellationToken), u => u.Version);

    [HttpPost("users/{id:guid}/activate")]
    [ProducesResponseType<UserListItemDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ActivateUserCommand(id), cancellationToken));

    [HttpPost("users/{id:guid}/deactivate")]
    [ProducesResponseType<UserListItemDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeactivateUserCommand(id), cancellationToken));

    [HttpPost("users/{id:guid}/resend-invitation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResendInvitation(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new ResendInvitationCommand(id), cancellationToken));

    [HttpGet("roles")]
    [ProducesResponseType<IReadOnlyList<RoleDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Roles(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetRolesQuery(), cancellationToken));

    [HttpGet("audit")]
    [ListEndpoint]
    [ProducesResponseType<PagedResult<AuditRecordDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Audit([FromQuery] string? actor, [FromQuery] string? action, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, [FromQuery] string? sort, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetAuditQuery(actor, action, from, to, sort, page, pageSize), cancellationToken));
}
