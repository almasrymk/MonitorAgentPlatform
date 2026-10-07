using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Devices.Enroll;

/// <summary>Enrollment = activation (D10, 04 section 4.1, 05 section 1.1).</summary>
[AllowAnonymousRequest]
public sealed record EnrollDeviceCommand(
    string ProductKey, string Fingerprint, string Hostname, string? OsFamily, string? OsName, string? OsVersion, string? Architecture,
    string? AgentVersion, int ProtocolVersion, string? LocationCode, string? LocalIp, string? MacAddress) : ICommand<EnrollmentResult>;

public sealed record EnrollmentLicense(string State, string? Token, DateTimeOffset? CheckAfter, JsonElement? SigningKeys);

public sealed record EnrollmentResult(
    Guid DeviceId, string TenantName, string LocationName, string DeviceSecret, string TokenUrl, string GatewayUrl, JsonElement CommandSigningKeys, EnrollmentLicense License);

internal sealed class EnrollDeviceCommandValidator : AbstractValidator<EnrollDeviceCommand>
{
    public EnrollDeviceCommandValidator()
    {
        RuleFor(x => x.ProductKey).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Fingerprint).NotEmpty().Length(8, 128).Matches("^[A-Za-z0-9_:.-]+$").WithMessage("Fingerprint must be 8-128 characters of letters, digits, '-', '_', ':' or '.'.");
        RuleFor(x => x.Hostname).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OsName).MaximumLength(200);
        RuleFor(x => x.OsVersion).MaximumLength(100);
        RuleFor(x => x.Architecture).MaximumLength(32);
        RuleFor(x => x.AgentVersion).MaximumLength(32);
        RuleFor(x => x.LocationCode).MaximumLength(32);
    }
}

internal sealed class EnrollDeviceCommandHandler(
    IAppDbContext db,
    IUnitOfWork unitOfWork,
    ILicensingGateway licensing,
    ITenantProvisioning provisioning,
    ITenantDirectory tenants,
    ILocationLookup locations,
    IEnrollmentCodeRedeemer codes,
    IDeviceSeats seats,
    IEnrollmentAttemptLog attempts,
    IDeviceSecretService secrets,
    IAuditLogger audit,
    ICurrentUser caller,
    ITenantScopeSetter scope,
    IMemoryCache cache,
    IOptions<AgentSettings> agent,
    ILicensingPolicy policy,
    TimeProvider clock) : ICommandHandler<EnrollDeviceCommand, EnrollmentResult>
{
    public static readonly Error RateLimited = Error.TooManyRequests("RATE_LIMITED", "Too many enrollments for this device. Try again later.");

    public async Task<Result<EnrollmentResult>> Handle(EnrollDeviceCommand request, CancellationToken cancellationToken)
    {
        // Enrollment arrives before any tenant is known: it runs in the system scope (allowed call site, 03 section 4).
        scope.RunAsSystem();
        var now = clock.GetUtcNow();
        var settings = agent.Value;

        if (request.ProtocolVersion < settings.SupportedProtocolMin || request.ProtocolVersion > settings.SupportedProtocolMax)
            return await FailAsync(null, request, DeviceErrors.ProtocolUnsupported, null, cancellationToken);

        if (!TakeFingerprintSlot(request.Fingerprint, settings.EnrollPerFingerprintPerHour))
            return await FailAsync(null, request, RateLimited, null, cancellationToken);

        // A new idempotency key per enrollment request: a later re-enrollment must reach the Licensing Platform again.
        var seat = await licensing.ActivateSeatAsync(
            new ActivateSeatRequest(request.ProductKey, request.Fingerprint, request.Hostname, request.AgentVersion ?? "unknown", request.OsName ?? request.OsFamily ?? "unknown", Guid.CreateVersion7().ToString("N")),
            cancellationToken);
        if (seat.IsFailure)
            return await FailAsync(null, request, seat.Error!, null, cancellationToken);

        var customer = await FindCustomerAsync(seat.Value.CustomerId, cancellationToken);
        var tenant = await provisioning.EnsureTenantAsync(seat.Value.CustomerId, customer?.Name ?? $"Customer {seat.Value.CustomerId.ToString()[..8]}", customer?.Country, cancellationToken);
        if (tenant.Status != "Active")
            return await FailAsync(tenant.TenantId, request, DeviceErrors.TenantNotActive, seat.Value, cancellationToken);

        Guid? locationId = null;
        if (!string.IsNullOrWhiteSpace(request.LocationCode))
        {
            locationId = await codes.RedeemAsync(tenant.TenantId, request.LocationCode, cancellationToken);
            if (locationId is null)
                return await FailAsync(tenant.TenantId, request, DeviceErrors.InvalidLocationCode, seat.Value, cancellationToken);
        }

        var info = new AgentInfo(request.Hostname, Device.ParseOsFamily(request.OsFamily), request.OsName, request.OsVersion, request.Architecture,
            request.AgentVersion, request.ProtocolVersion, request.LocalIp, caller.IpAddress, request.MacAddress);
        var secret = secrets.NewSecret();
        var grace = policy.UnlicensedGrace;

        var device = await db.Set<Device>().SingleOrDefaultAsync(d => d.TenantId == tenant.TenantId && d.Fingerprint == request.Fingerprint, cancellationToken);
        if (device is null)
        {
            var target = locationId ?? (await locations.DefaultAsync(tenant.TenantId, cancellationToken))?.Id
                ?? throw new InvalidOperationException($"Tenant {tenant.TenantId} has no default location.");
            device = Device.Enroll(tenant.TenantId, target, request.Fingerprint, info, now);
            db.Set<Device>().Add(device);
            db.Set<DeviceCredential>().Add(DeviceCredential.Issue(device.Id, tenant.TenantId, secret.Hash, now));
            db.Set<DeviceState>().Add(DeviceState.Create(device.Id, tenant.TenantId, target, info.OsFamily, LicenseStateValue.Licensed, now));
            audit.Add("device.enrolled", "Device", device.Id.ToString(), $"{device.Name} ({request.Fingerprint})");
        }
        else
        {
            // Same fingerprint: reinstall or re-enrollment. A new secret is issued and the old one revoked;
            // a retired device comes back in its previous location.
            if (device.IsRetired)
                device.Reactivate(info, now);
            else
                device.UpdateAgentInfo(info);

            var credential = await db.Set<DeviceCredential>().SingleOrDefaultAsync(c => c.DeviceId == device.Id, cancellationToken);
            if (credential is null)
                db.Set<DeviceCredential>().Add(DeviceCredential.Issue(device.Id, tenant.TenantId, secret.Hash, now));
            else
                credential.Replace(secret.Hash, now);

            var state = await db.Set<DeviceState>().SingleOrDefaultAsync(s => s.DeviceId == device.Id, cancellationToken);
            if (state is null)
                db.Set<DeviceState>().Add(DeviceState.Create(device.Id, tenant.TenantId, device.LocationId, info.OsFamily, LicenseStateValue.Licensed, now));
            else
            {
                state.SetOsFamily(info.OsFamily);
                state.SetLicense(LicenseStateValue.Licensed, now, grace);
            }

            audit.Add("device.reenrolled", "Device", device.Id.ToString(), $"{device.Name} ({request.Fingerprint})");
        }

        await seats.RecordLicensedAsync(device.Id, tenant.TenantId, request.Fingerprint, seat.Value, now, cancellationToken);
        attempts.AddSuccess(tenant.TenantId, request.ProductKey, request.Fingerprint, request.Hostname, caller.IpAddress, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var tenantName = (await tenants.FindAsync(tenant.TenantId, cancellationToken))?.Name ?? string.Empty;
        var locationName = (await locations.GetAsync([device.LocationId], cancellationToken)).GetValueOrDefault(device.LocationId)?.Name ?? string.Empty;
        var keys = await licensing.GetSigningKeysJsonAsync(cancellationToken);
        return new EnrollmentResult(
            device.Id, tenantName, locationName, secret.Secret, "/api/agent/v1/token", settings.GatewayUrl, NoCommandKeys(),
            new EnrollmentLicense("Licensed", seat.Value.Token, seat.Value.CheckAfter, keys.IsSuccess ? Parse(keys.Value) : null));
    }

    /// <summary>
    /// Records the failed attempt; when a seat was already taken it is released again (tenant not active, bad location
    /// code), so a failed enrollment never consumes a licence.
    /// </summary>
    private async Task<Result<EnrollmentResult>> FailAsync(Guid? tenantId, EnrollDeviceCommand request, Error error, SeatActivation? seat, CancellationToken ct)
    {
        if (seat is not null && !seat.AlreadyActivated)
            await licensing.ReleaseSeatAsync(seat.LicenseId, request.Fingerprint, ct);
        await attempts.WriteFailureAsync(tenantId, request.ProductKey, request.Fingerprint, request.Hostname, caller.IpAddress, error.Code, clock.GetUtcNow(), ct);
        return error;
    }

    /// <summary>Command signing keys arrive with remote actions (M11); until then the set is empty.</summary>
    private static JsonElement NoCommandKeys() => Parse("{\"keys\":[]}")!.Value;

    private static JsonElement? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private bool TakeFingerprintSlot(string fingerprint, int perHour)
    {
        var key = $"enroll:fp:{fingerprint}";
        var count = cache.GetOrCreate(key, e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return new int[1];
        })!;
        return Interlocked.Increment(ref count[0]) <= perHour;
    }

    private async Task<LicensingCustomer?> FindCustomerAsync(Guid customerId, CancellationToken ct)
    {
        for (var page = 1; page <= 50; page++)
        {
            var result = await licensing.ListCustomersAsync(page, 200, null, ct);
            if (result.IsFailure)
                return null;
            var match = result.Value.Items.FirstOrDefault(c => c.Id == customerId);
            if (match is not null || result.Value.Items.Count < 200)
                return match;
        }

        return null;
    }
}
