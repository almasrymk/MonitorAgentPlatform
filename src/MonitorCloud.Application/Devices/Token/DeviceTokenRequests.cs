using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Devices;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Devices.Token;

public sealed record DeviceTokenResult(string AccessToken, int ExpiresIn);

/// <summary>Device id + secret -> device JWT (05 section 1.2).</summary>
[AllowAnonymousRequest]
public sealed record IssueDeviceTokenCommand(Guid DeviceId, string DeviceSecret) : ICommand<DeviceTokenResult>;

internal sealed class IssueDeviceTokenCommandValidator : AbstractValidator<IssueDeviceTokenCommand>
{
    public IssueDeviceTokenCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.DeviceSecret).NotEmpty().MaximumLength(100);
    }
}

internal sealed class IssueDeviceTokenCommandHandler(
    IAppDbContext db,
    IUnitOfWork unitOfWork,
    IDeviceSecretService secrets,
    IDeviceTokenService tokens,
    ITenantDirectory tenants,
    IAuditLogger audit,
    ITenantScopeSetter scope,
    TimeProvider clock) : ICommandHandler<IssueDeviceTokenCommand, DeviceTokenResult>
{
    public async Task<Result<DeviceTokenResult>> Handle(IssueDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        // The device is not authenticated yet: the system scope reads its credential (allowed call site, 03 section 4).
        scope.RunAsSystem();
        var now = clock.GetUtcNow();
        var credential = await db.Set<DeviceCredential>().SingleOrDefaultAsync(c => c.DeviceId == request.DeviceId, cancellationToken);
        var device = credential is null ? null : await db.Set<Device>().SingleOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken);

        // Same answer for unknown device, wrong secret, revoked credential, retired device and blocked id.
        if (credential is null || device is null)
            return DeviceErrors.InvalidCredential;
        if (credential.IsBlocked(now))
            return DeviceErrors.InvalidCredential;

        var tenant = await tenants.FindAsync(device.TenantId, cancellationToken);
        var valid = !credential.IsRevoked && !device.IsRetired && tenant is not null && tenant.Status != "Archived"
            && secrets.Verify(request.DeviceSecret, credential.SecretHash);
        if (!valid)
        {
            credential.RegisterFailure(now);
            audit.Add("device.token.failed", "Device", device.Id.ToString(), credential.IsBlocked(now) ? "Blocked for 10 minutes" : null, success: false);
            // The failure count must persist although the command fails.
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return DeviceErrors.InvalidCredential;
        }

        credential.RegisterSuccess();
        var token = tokens.CreateDeviceToken(device.Id, device.TenantId, device.Fingerprint, now);
        return new DeviceTokenResult(token.Token, (int)(token.ExpiresAt - now).TotalSeconds);
    }
}

public sealed record RotatedSecret(string DeviceSecret);

/// <summary>Issues a new secret for the calling device; the old one stops working at once.</summary>
[AllowDevice]
public sealed record RotateDeviceCredentialCommand : ICommand<RotatedSecret>;

internal sealed class RotateDeviceCredentialCommandValidator : AbstractValidator<RotateDeviceCredentialCommand>;

internal sealed class RotateDeviceCredentialCommandHandler(
    IAppDbContext db, IDeviceSecretService secrets, ICurrentUser caller, IAuditLogger audit, ITenantScopeSetter scope, TimeProvider clock)
    : ICommandHandler<RotateDeviceCredentialCommand, RotatedSecret>
{
    public async Task<Result<RotatedSecret>> Handle(RotateDeviceCredentialCommand request, CancellationToken cancellationToken)
    {
        if (caller.DeviceId is not { } deviceId || caller.TenantId is not { } tenantId)
            return DeviceErrors.InvalidCredential;
        scope.RunAsTenant(tenantId);
        var credential = await db.Set<DeviceCredential>().SingleOrDefaultAsync(c => c.DeviceId == deviceId, cancellationToken);
        if (credential is null || credential.IsRevoked)
            return DeviceErrors.InvalidCredential;

        var secret = secrets.NewSecret();
        credential.Replace(secret.Hash, clock.GetUtcNow());
        audit.Add("device.credential.rotated", "Device", deviceId.ToString());
        return new RotatedSecret(secret.Secret);
    }
}
