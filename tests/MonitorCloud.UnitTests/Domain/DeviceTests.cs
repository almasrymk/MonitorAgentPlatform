using MonitorCloud.Domain.Devices;
using MonitorCloud.Domain.Licensing;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared;

namespace MonitorCloud.UnitTests.Domain;

public sealed class DeviceTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly Guid Location = Guid.CreateVersion7();

    private static AgentInfo Info(string hostname = "WEB-SRV-01", string? os = "Windows Server 2022") =>
        new(hostname, OsFamily.Windows, os, "10.0.20348", "x64", "1.1.0", 1, "10.0.0.5", "203.0.113.5", "00-11-22-33-44-55");

    [Fact]
    public void Enroll_takes_the_hostname_as_name_and_raises_DeviceEnrolledV1()
    {
        var device = Device.Enroll(Tenant, Location, "ma-0001-abcdef", Info(), Now);

        device.Name.ShouldBe("WEB-SRV-01");
        device.Status.ShouldBe(DeviceStatus.Active);
        device.EnrolledAt.ShouldBe(Now);
        device.OsName.ShouldBe("Windows Server 2022");
        var enrolled = device.DomainEvents.OfType<DeviceEnrolledV1>().Single();
        enrolled.DeviceId.ShouldBe(device.Id);
        enrolled.LocationId.ShouldBe(Location);
    }

    [Fact]
    public void Enroll_rejects_an_empty_fingerprint()
    {
        Should.Throw<DomainException>(() => Device.Enroll(Tenant, Location, "", Info(), Now));
    }

    [Fact]
    public void Rename_trims_and_validates()
    {
        var device = Device.Enroll(Tenant, Location, "ma-0001-abcdef", Info(), Now);

        device.Rename("  Front Desk PC  ");

        device.Name.ShouldBe("Front Desk PC");
        Should.Throw<DomainException>(() => device.Rename(" "));
        Should.Throw<DomainException>(() => device.Rename(new string('x', Device.NameMaxLength + 1)));
    }

    [Fact]
    public void Move_raises_an_event_only_when_the_location_changes()
    {
        var device = Device.Enroll(Tenant, Location, "ma-0001-abcdef", Info(), Now);
        device.ClearDomainEvents();
        var target = Guid.CreateVersion7();

        device.MoveTo(Location, Now);
        device.DomainEvents.ShouldBeEmpty();
        device.MoveTo(target, Now);

        device.LocationId.ShouldBe(target);
        var moved = device.DomainEvents.OfType<DeviceMovedV1>().Single();
        moved.FromLocationId.ShouldBe(Location);
        moved.ToLocationId.ShouldBe(target);
    }

    [Fact]
    public void Retire_raises_an_event_and_cannot_happen_twice()
    {
        var device = Device.Enroll(Tenant, Location, "ma-0001-abcdef", Info(), Now);
        device.ClearDomainEvents();

        device.Retire(Now.AddDays(1));

        device.IsRetired.ShouldBeTrue();
        device.RetiredAt.ShouldBe(Now.AddDays(1));
        device.DomainEvents.OfType<DeviceRetiredV1>().Single().Fingerprint.ShouldBe("ma-0001-abcdef");
        Should.Throw<DomainException>(() => device.Retire(Now.AddDays(2))).Error.Code.ShouldBe("DEVICE_RETIRED");
        Should.Throw<DomainException>(() => device.RequestUnlicense(Now.AddDays(2)));
    }

    [Fact]
    public void Reactivate_brings_a_retired_device_back_in_its_previous_location()
    {
        var device = Device.Enroll(Tenant, Location, "ma-0001-abcdef", Info(), Now);
        device.Retire(Now.AddDays(1));
        device.ClearDomainEvents();

        device.Reactivate(Info("WEB-SRV-01B", "Windows Server 2025"), Now.AddDays(2));

        device.Status.ShouldBe(DeviceStatus.Active);
        device.RetiredAt.ShouldBeNull();
        device.LocationId.ShouldBe(Location);
        device.Hostname.ShouldBe("WEB-SRV-01B");
        device.OsName.ShouldBe("Windows Server 2025");
        device.DomainEvents.OfType<DeviceEnrolledV1>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Update_agent_info_keeps_the_given_name()
    {
        var device = Device.Enroll(Tenant, Location, "ma-0001-abcdef", Info(), Now);
        device.Rename("Reception");

        device.UpdateAgentInfo(Info("NEW-HOST", "Windows 11"));

        device.Name.ShouldBe("Reception");
        device.Hostname.ShouldBe("NEW-HOST");
    }

    [Fact]
    public void Request_unlicense_raises_the_seat_release_event()
    {
        var device = Device.Enroll(Tenant, Location, "ma-0001-abcdef", Info(), Now);
        device.ClearDomainEvents();

        device.RequestUnlicense(Now);

        device.DomainEvents.OfType<DeviceUnlicenseRequestedV1>().Single().Fingerprint.ShouldBe("ma-0001-abcdef");
    }

    [Theory]
    [InlineData("Windows", OsFamily.Windows)]
    [InlineData("windows", OsFamily.Windows)]
    [InlineData("Linux", OsFamily.Linux)]
    [InlineData("macOS", OsFamily.MacOS)]
    [InlineData("darwin", OsFamily.MacOS)]
    [InlineData("osx", OsFamily.MacOS)]
    [InlineData("FreeBSD", OsFamily.Other)]
    [InlineData(null, OsFamily.Other)]
    public void Os_family_is_parsed(string? value, OsFamily expected) => Device.ParseOsFamily(value).ShouldBe(expected);
}

public sealed class DeviceCredentialTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;

    private static DeviceCredential New() => DeviceCredential.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), "HASH-1", Now);

    [Fact]
    public void Five_failures_within_ten_minutes_block_for_ten_minutes()
    {
        var credential = New();
        for (var i = 0; i < 4; i++)
            credential.RegisterFailure(Now.AddMinutes(i));
        credential.IsBlocked(Now.AddMinutes(4)).ShouldBeFalse();

        credential.RegisterFailure(Now.AddMinutes(4));

        credential.IsBlocked(Now.AddMinutes(5)).ShouldBeTrue();
        credential.IsBlocked(Now.AddMinutes(14).AddSeconds(1)).ShouldBeFalse();
    }

    [Fact]
    public void Failures_outside_the_window_start_a_new_count()
    {
        var credential = New();
        for (var i = 0; i < 4; i++)
            credential.RegisterFailure(Now.AddMinutes(i));

        credential.RegisterFailure(Now.AddMinutes(11));

        credential.FailedAttempts.ShouldBe(1);
        credential.IsBlocked(Now.AddMinutes(11)).ShouldBeFalse();
    }

    [Fact]
    public void Success_resets_the_count()
    {
        var credential = New();
        credential.RegisterFailure(Now);
        credential.RegisterFailure(Now);

        credential.RegisterSuccess();

        credential.FailedAttempts.ShouldBe(0);
        credential.FirstFailureAt.ShouldBeNull();
    }

    [Fact]
    public void Replace_issues_a_new_hash_and_clears_revocation_and_blocks()
    {
        var credential = New();
        credential.Revoke(Now);
        for (var i = 0; i < 5; i++)
            credential.RegisterFailure(Now);

        credential.Replace("HASH-2", Now.AddMinutes(1));

        credential.SecretHash.ShouldBe("HASH-2");
        credential.RotatedAt.ShouldBe(Now.AddMinutes(1));
        credential.IsRevoked.ShouldBeFalse();
        credential.IsBlocked(Now.AddMinutes(1)).ShouldBeFalse();
    }

    [Fact]
    public void Revoke_keeps_the_first_time()
    {
        var credential = New();
        credential.Revoke(Now);
        credential.Revoke(Now.AddDays(1));

        credential.RevokedAt.ShouldBe(Now);
        credential.IsRevoked.ShouldBeTrue();
    }
}

public sealed class DeviceStateTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;
    private static readonly TimeSpan Grace = TimeSpan.FromDays(14);

    private static DeviceState New(LicenseStateValue license = LicenseStateValue.Licensed) =>
        DeviceState.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), OsFamily.Linux, license, Now);

    [Fact]
    public void A_new_state_is_offline_and_unknown()
    {
        var state = New();

        state.Connection.ShouldBe(ConnectionState.Offline);
        state.Health.ShouldBe(DeviceHealth.Unknown);
        state.UnlicensedSince.ShouldBeNull();
        New(LicenseStateValue.Unlicensed).UnlicensedSince.ShouldBe(Now);
    }

    [Fact]
    public void Coming_online_sets_connected_since_and_last_seen_and_recalculates()
    {
        var state = New();

        state.SetConnection(ConnectionState.Online, Now, Grace);

        state.ConnectedSince.ShouldBe(Now.UtcDateTime);
        state.LastSeenAt.ShouldBe(Now.UtcDateTime);
        state.Health.ShouldBe(DeviceHealth.Healthy);

        state.SetConnection(ConnectionState.Offline, Now.AddMinutes(5), Grace);
        state.ConnectedSince.ShouldBeNull();
        state.Health.ShouldBe(DeviceHealth.Unknown);
    }

    [Fact]
    public void Open_alerts_drive_warning_and_critical()
    {
        var state = New();
        state.SetConnection(ConnectionState.Online, Now, Grace);

        state.SetOpenAlerts(0, 2, Now, Grace);
        state.Health.ShouldBe(DeviceHealth.Warning);
        state.SetOpenAlerts(1, 2, Now, Grace);
        state.Health.ShouldBe(DeviceHealth.Critical);
        state.SetOpenAlerts(-1, -1, Now, Grace);
        state.OpenCritical.ShouldBe(0);
        state.Health.ShouldBe(DeviceHealth.Healthy);
    }

    [Fact]
    public void Losing_the_licence_starts_the_grace_period_once_and_licensing_again_clears_it()
    {
        var state = New();
        state.SetConnection(ConnectionState.Online, Now, Grace);

        state.SetLicense(LicenseStateValue.Unlicensed, Now.AddDays(1), Grace);
        state.SetLicense(LicenseStateValue.Unlicensed, Now.AddDays(2), Grace);
        state.UnlicensedSince.ShouldBe(Now.AddDays(1));
        state.Health.ShouldBe(DeviceHealth.Healthy);

        state.Recalculate(Now.AddDays(15), Grace);
        state.Health.ShouldBe(DeviceHealth.Unknown);

        state.SetLicense(LicenseStateValue.Licensed, Now.AddDays(16), Grace);
        state.UnlicensedSince.ShouldBeNull();
        state.Health.ShouldBe(DeviceHealth.Healthy);
    }

    [Fact]
    public void Metrics_move_and_os_are_stored()
    {
        var state = New();

        state.SetMetrics(12.5m, 40m, 70.25m, 3600, Now);
        state.MoveTo(Guid.Empty, Now);
        state.SetOsFamily(OsFamily.MacOS);
        state.Seen(Now.AddMinutes(1));

        state.CpuPercent.ShouldBe(12.5m);
        state.UptimeSeconds.ShouldBe(3600);
        state.LastTelemetryAt.ShouldBe(Now.UtcDateTime);
        state.LocationId.ShouldBe(Guid.Empty);
        state.OsFamily.ShouldBe(OsFamily.MacOS);
        state.LastSeenAt.ShouldBe(Now.AddMinutes(1).UtcDateTime);
    }

    [Fact]
    public void Seed_as_sets_a_past_state()
    {
        var state = New();

        state.SeedAs(ConnectionState.Online, Now.AddMinutes(-1), Now.AddDays(-3), Now, Grace);

        state.LastSeenAt.ShouldBe(Now.AddMinutes(-1).UtcDateTime);
        state.ConnectedSince.ShouldBe(Now.AddDays(-3).UtcDateTime);
        state.Health.ShouldBe(DeviceHealth.Healthy);
    }
}

/// <summary>The full truth table of 02 section 6 / D14 / D19.</summary>
public sealed class DeviceHealthCalculatorTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;
    private static readonly TimeSpan Grace = TimeSpan.FromDays(14);

    public static TheoryData<ConnectionState, int, int, LicenseStateValue, int?, DeviceHealth> Table()
    {
        var data = new TheoryData<ConnectionState, int, int, LicenseStateValue, int?, DeviceHealth>();
        foreach (var connection in new[] { ConnectionState.Online, ConnectionState.Offline })
        {
            foreach (var (critical, warning) in new[] { (0, 0), (0, 1), (1, 0), (1, 1) })
            {
                // Licensed; unlicensed inside the grace period (5 days ago); unlicensed after it (20 days ago).
                foreach (var (license, daysAgo) in new (LicenseStateValue, int?)[] { (LicenseStateValue.Licensed, null), (LicenseStateValue.Unlicensed, 5), (LicenseStateValue.Unlicensed, 20) })
                {
                    var expected = connection == ConnectionState.Offline || daysAgo == 20
                        ? DeviceHealth.Unknown
                        : critical > 0 ? DeviceHealth.Critical : warning > 0 ? DeviceHealth.Warning : DeviceHealth.Healthy;
                    data.Add(connection, critical, warning, license, daysAgo, expected);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Table))]
    public void Health_follows_the_rule_table(ConnectionState connection, int critical, int warning, LicenseStateValue license, int? unlicensedDaysAgo, DeviceHealth expected)
    {
        DateTimeOffset? since = unlicensedDaysAgo is { } d ? Now.AddDays(-d) : null;

        DeviceHealthCalculator.Calculate(connection, critical, warning, license, since, Now, Grace).ShouldBe(expected);
    }

    [Fact]
    public void The_grace_period_ends_exactly_at_its_length() =>
        DeviceHealthCalculator.Calculate(ConnectionState.Online, 0, 0, LicenseStateValue.Unlicensed, Now.Subtract(Grace), Now, Grace).ShouldBe(DeviceHealth.Unknown);
}

public sealed class LocationEnrollmentCodeTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;

    private static LocationEnrollmentCode New(TimeSpan? lifetime = null, int? maxUses = null) =>
        LocationEnrollmentCode.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "HASH", "LOCABC23", lifetime ?? TimeSpan.FromHours(24), maxUses, null, Now);

    [Fact]
    public void A_code_is_usable_until_it_expires()
    {
        var code = New();

        code.CanBeUsed(Now.AddHours(23)).ShouldBeTrue();
        code.CanBeUsed(Now.AddHours(24)).ShouldBeFalse();
        code.ExpiresAt.ShouldBe(Now.AddHours(24));
    }

    [Fact]
    public void Max_uses_are_counted()
    {
        var code = New(maxUses: 2);

        code.Use(Now);
        code.Use(Now);

        code.Uses.ShouldBe(2);
        code.CanBeUsed(Now).ShouldBeFalse();
        Should.Throw<DomainException>(() => code.Use(Now)).Error.Code.ShouldBe("ENROLL_INVALID_LOCATION_CODE");
    }

    [Fact]
    public void A_revoked_code_cannot_be_used()
    {
        var code = New();

        code.Revoke(Now);
        code.Revoke(Now.AddHours(1));

        code.RevokedAt.ShouldBe(Now);
        code.CanBeUsed(Now).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31 * 24)]
    public void Lifetime_must_be_within_30_days(int hours) =>
        Should.Throw<DomainException>(() => New(TimeSpan.FromHours(hours)));

    [Fact]
    public void Max_uses_must_be_positive() => Should.Throw<DomainException>(() => New(maxUses: 0));
}

public sealed class EnrollmentAttemptTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;

    [Fact]
    public void Only_the_first_six_characters_of_the_product_key_are_kept()
    {
        var attempt = EnrollmentAttempt.Record(null, "abc234-XYZ789-QQQQQQ-WWWWWW-EEEEEE", "fp-00000001", "HOST", "198.51.100.7", "LIC_INVALID_LICENSE", Now);

        attempt.KeyPrefix.ShouldBe("ABC234");
        attempt.Succeeded.ShouldBeFalse();
        attempt.ErrorCode.ShouldBe("LIC_INVALID_LICENSE");
        attempt.TenantId.ShouldBeNull();
    }

    [Fact]
    public void Missing_values_and_long_values_are_stored_safely()
    {
        var attempt = EnrollmentAttempt.Record(Guid.CreateVersion7(), "ab", null, new string('h', 300), null, null, Now);

        attempt.KeyPrefix.ShouldBe("AB");
        attempt.DeviceFingerprint.ShouldBe(string.Empty);
        attempt.Hostname.Length.ShouldBe(200);
        attempt.Succeeded.ShouldBeTrue();
    }
}

public sealed class InventoryDocumentTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;

    [Fact]
    public void Replace_writes_only_when_the_hash_changes()
    {
        var document = InventoryDocument.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), InventoryKind.Hardware, [1, 2], "H1", Now);

        document.Replace([9], "H1", Now.AddHours(1)).ShouldBeFalse();
        document.UpdatedAt.ShouldBe(Now);
        document.Replace([3], "H2", Now.AddHours(2)).ShouldBeTrue();

        document.Json.ShouldBe(new byte[] { 3 });
        document.Hash.ShouldBe("H2");
        document.UpdatedAt.ShouldBe(Now.AddHours(2));
    }
}
