using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MonitorCloud.Application.Licensing.Contracts;

namespace MonitorCloud.ContractTests;

/// <summary>
/// One suite of behaviours executed against every adapter (09 section 6): the fake and the live Licensing Platform
/// must answer the same.
/// </summary>
public abstract class LicensingGatewayContract
{
    protected abstract IContractFixture Fixture { get; }

    /// <summary>Skips the test (reported as skipped) when the adapter is not configured.</summary>
    protected virtual void Ready()
    {
    }

    private ILicensingGateway Gateway => Fixture.Gateway;

    private Task<MonitorCloud.SharedKernel.Result<SeatActivation>> Activate(string key, string deviceId, string? idempotencyKey = null) =>
        Gateway.ActivateSeatAsync(new ActivateSeatRequest(key, deviceId, "Contract Device", "1.0.0", "Windows", idempotencyKey ?? Guid.NewGuid().ToString("N")), CancellationToken.None);

    [SkippableFact]
    public async Task Activating_a_new_device_returns_the_seat_with_licence_customer_and_subscription_ids()
    {
        Ready();
        var device = Fixture.NewDeviceId();

        var result = await Activate(Fixture.ActiveKey, device);

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        var seat = result.Value;
        seat.AlreadyActivated.ShouldBeFalse();
        seat.LicenseId.ShouldNotBe(Guid.Empty);
        seat.CustomerId.ShouldBe(Fixture.CustomerId);
        seat.SubscriptionId.ShouldNotBe(Guid.Empty);
        seat.Token.ShouldNotBeNullOrWhiteSpace();
        seat.Kid.ShouldNotBeNullOrWhiteSpace();
        seat.CheckAfter.ShouldBeLessThan(seat.OfflineValidUntil);
        seat.Features.ShouldContain("monitoring");
        await Gateway.ReleaseSeatAsync(seat.LicenseId, device, CancellationToken.None);
    }

    [SkippableFact]
    public async Task Activating_the_same_device_again_is_idempotent()
    {
        Ready();
        var device = Fixture.NewDeviceId();
        var first = (await Activate(Fixture.ActiveKey, device)).Value;

        var second = await Activate(Fixture.ActiveKey, device);

        second.Value.AlreadyActivated.ShouldBeTrue();
        second.Value.ActiveActivations.ShouldBe(first.ActiveActivations);
        await Gateway.ReleaseSeatAsync(first.LicenseId, device, CancellationToken.None);
    }

    [SkippableFact]
    public async Task The_same_idempotency_key_replays_the_first_answer()
    {
        Ready();
        var device = Fixture.NewDeviceId();
        var key = Guid.NewGuid().ToString("N");
        var first = (await Activate(Fixture.ActiveKey, device, key)).Value;

        var replay = (await Activate(Fixture.ActiveKey, device, key)).Value;

        replay.AlreadyActivated.ShouldBe(first.AlreadyActivated);
        replay.LicenseNumber.ShouldBe(first.LicenseNumber);
        await Gateway.ReleaseSeatAsync(first.LicenseId, device, CancellationToken.None);
    }

    [SkippableFact]
    public async Task The_seat_limit_is_enforced_with_LIC_ACTIVATION_LIMIT_REACHED()
    {
        Ready();
        Skip.If(Fixture.LimitedKey is null, "No limited licence configured.");
        var devices = new[] { Fixture.NewDeviceId(), Fixture.NewDeviceId() };
        var seats = new List<SeatActivation>();
        foreach (var device in devices)
            seats.Add((await Activate(Fixture.LimitedKey!, device)).Value);

        var third = await Activate(Fixture.LimitedKey!, Fixture.NewDeviceId());

        third.Error!.Code.ShouldBe("LIC_ACTIVATION_LIMIT_REACHED");
        foreach (var (seat, device) in seats.Zip(devices))
            await Gateway.ReleaseSeatAsync(seat.LicenseId, device, CancellationToken.None);
    }

    [SkippableFact]
    public async Task An_unknown_key_is_LIC_INVALID_LICENSE()
    {
        Ready();
        (await Activate("AAAAAA-BBBBBB-CCCCCC-DDDDDD-EEEEEE", Fixture.NewDeviceId())).Error!.Code.ShouldBe("LIC_INVALID_LICENSE");
    }

    [SkippableFact]
    public async Task An_invalid_device_id_is_LIC_INVALID_DEVICE()
    {
        Ready();
        (await Activate(Fixture.ActiveKey, "x")).Error!.Code.ShouldBe("LIC_INVALID_DEVICE");
    }

    [SkippableFact]
    public async Task A_suspended_licence_is_LIC_SUSPENDED()
    {
        Ready();
        Skip.If(Fixture.SuspendedKey is null, "No suspended licence configured.");

        (await Activate(Fixture.SuspendedKey!, Fixture.NewDeviceId())).Error!.Code.ShouldBe("LIC_SUSPENDED");
    }

    [SkippableFact]
    public async Task An_expired_licence_is_refused()
    {
        Ready();
        Skip.If(Fixture.ExpiredKey is null, "No expired licence configured.");

        (await Activate(Fixture.ExpiredKey!, Fixture.NewDeviceId())).Error!.Code.ShouldBeOneOf("LIC_EXPIRED", "LIC_SUBSCRIPTION_EXPIRED");
    }

    [SkippableFact]
    public async Task Refresh_returns_a_new_token_and_release_frees_the_seat_for_a_new_activation()
    {
        Ready();
        var device = Fixture.NewDeviceId();
        var seat = (await Activate(Fixture.ActiveKey, device)).Value;

        var refreshed = await Gateway.RefreshSeatAsync(seat.LicenseId, device, "1.0.1", "Windows", CancellationToken.None);
        var released = await Gateway.ReleaseSeatAsync(seat.LicenseId, device, CancellationToken.None);
        var releasedAgain = await Gateway.ReleaseSeatAsync(seat.LicenseId, device, CancellationToken.None);
        var refreshAfterRelease = await Gateway.RefreshSeatAsync(seat.LicenseId, device, null, null, CancellationToken.None);
        var reactivated = await Activate(Fixture.ActiveKey, device);

        refreshed.IsSuccess.ShouldBeTrue(refreshed.Error?.Code);
        refreshed.Value.Token.ShouldNotBeNullOrWhiteSpace();
        released.IsSuccess.ShouldBeTrue();
        releasedAgain.Error!.Code.ShouldBe("LIC_DEVICE_NOT_ACTIVATED");
        refreshAfterRelease.Error!.Code.ShouldBe("LIC_DEVICE_NOT_ACTIVATED");
        reactivated.Value.AlreadyActivated.ShouldBeFalse();
        await Gateway.ReleaseSeatAsync(seat.LicenseId, device, CancellationToken.None);
    }

    [SkippableFact]
    public async Task Entitlements_describe_the_customers_subscription_and_licences()
    {
        Ready();
        var result = await Gateway.GetEntitlementsAsync(Fixture.CustomerId, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.Error?.Code);
        result.Value.CustomerId.ShouldBe(Fixture.CustomerId);
        result.Value.CustomerStatus.ShouldBe("Active");
        var subscription = result.Value.Subscriptions.ShouldHaveSingleItem();
        subscription.PlanCode.ShouldNotBeNullOrWhiteSpace();
        subscription.Features.ShouldContain("monitoring");
        var license = result.Value.Licenses.ShouldHaveSingleItem();
        license.SubscriptionId.ShouldBe(subscription.Id);
        license.ActiveActivations.ShouldBeGreaterThanOrEqualTo(0);
    }

    [SkippableFact]
    public async Task Unknown_customers_are_not_found()
    {
        Ready();
        (await Gateway.GetEntitlementsAsync(Guid.NewGuid(), CancellationToken.None)).Error!.Kind.ShouldBe(MonitorCloud.SharedKernel.ErrorKind.NotFound);
    }

    [SkippableFact]
    public async Task Customers_are_listed_with_paging()
    {
        Ready();
        var page = await Gateway.ListCustomersAsync(1, 200, null, CancellationToken.None);

        page.IsSuccess.ShouldBeTrue(page.Error?.Code);
        page.Value.Items.ShouldContain(c => c.Id == Fixture.CustomerId);
        page.Value.PageSize.ShouldBe(200);
    }

    [SkippableFact]
    public async Task Plans_are_published()
    {
        Ready();
        var plans = await Gateway.ListPlansAsync(CancellationToken.None);

        plans.IsSuccess.ShouldBeTrue(plans.Error?.Code);
        Fixture.PlanCodes.Except(plans.Value.Select(p => p.Code)).ShouldBeEmpty();
        plans.Value.ShouldAllBe(p => p.Features.Count > 0);
    }

    [SkippableFact]
    public async Task The_change_feed_is_ordered_and_the_cursor_moves_forward()
    {
        Ready();
        Fixture.AppendChange();
        var items = new List<ChangeItem>();
        string? cursor = null;
        for (var i = 0; i < 200; i++)
        {
            var page = await Gateway.GetChangesAsync(cursor, 50, CancellationToken.None);
            page.IsSuccess.ShouldBeTrue(page.Error?.Code);
            items.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
            if (!page.Value.HasMore)
                break;
        }

        items.ShouldNotBeEmpty();
        items.Select(i => i.OccurredAt).ShouldBe(items.Select(i => i.OccurredAt).Order().ToList());
        var end = await Gateway.GetChangesAsync(cursor, 50, CancellationToken.None);
        end.Value.Items.ShouldBeEmpty();
        end.Value.HasMore.ShouldBeFalse();
    }

    [SkippableFact]
    public async Task Signing_keys_verify_the_returned_licence_token()
    {
        Ready();
        var device = Fixture.NewDeviceId();
        var seat = (await Activate(Fixture.ActiveKey, device)).Value;
        var keys = await Gateway.GetSigningKeysJsonAsync(CancellationToken.None);

        var key = JsonDocument.Parse(keys.Value).RootElement.GetProperty("keys").EnumerateArray().Single(k => k.GetProperty("kid").GetString() == seat.Kid);
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Base64UrlEncoder.DecodeBytes(key.GetProperty("x").GetString()), Y = Base64UrlEncoder.DecodeBytes(key.GetProperty("y").GetString()) },
        });
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(seat.Token, new TokenValidationParameters
        {
            IssuerSigningKey = new ECDsaSecurityKey(ecdsa) { KeyId = seat.Kid },
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
        });

        validation.IsValid.ShouldBeTrue(validation.Exception?.Message);
        var token = (JsonWebToken)validation.SecurityToken;
        token.GetClaim("sub").Value.ShouldBe(device);
        token.GetClaim("lid").Value.ShouldBe(seat.LicenseId.ToString());
        token.GetClaim("cid").Value.ShouldBe(Fixture.CustomerId.ToString());
        await Gateway.ReleaseSeatAsync(seat.LicenseId, device, CancellationToken.None);
    }

    [SkippableFact]
    public async Task A_rate_limited_answer_maps_to_RATE_LIMITED()
    {
        Ready();
        Skip.If(!Fixture.CanSimulateRateLimit, "Rate limiting cannot be triggered against this adapter.");
        Fixture.SimulateRateLimit();

        (await Gateway.ListPlansAsync(CancellationToken.None)).Error!.Code.ShouldBe("RATE_LIMITED");
    }
}
