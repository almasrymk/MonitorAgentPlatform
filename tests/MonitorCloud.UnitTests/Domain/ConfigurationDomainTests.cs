using MonitorCloud.Application.Configuration;
using MonitorCloud.Domain.Configuration;
using MonitorCloud.Domain.Monitoring;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared;

namespace MonitorCloud.UnitTests.Domain;

public sealed class ConfigurationDomainTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly Guid Device = Guid.CreateVersion7();

    [Fact]
    public void A_configuration_starts_at_version_1_and_every_change_raises_the_version_and_an_event()
    {
        var configuration = DeviceConfiguration.Create(Device, Tenant, "{}", Now);
        configuration.Version.ShouldBe(1);
        configuration.DomainEvents.ShouldBeEmpty();

        configuration.Update("{\"a\":1}", Guid.CreateVersion7(), Now.AddMinutes(1));
        configuration.Bump(null, Now.AddMinutes(2));

        configuration.Version.ShouldBe(3);
        configuration.DocumentJson.ShouldBe("{\"a\":1}");
        configuration.DomainEvents.OfType<DeviceConfigurationChangedV1>().Select(e => e.Version).ShouldBe([2, 3]);
        Should.Throw<DomainException>(() => configuration.Update(" ", null, Now));
    }

    [Fact]
    public void The_ack_keeps_the_applied_version_when_a_later_one_is_rejected()
    {
        var ack = DeviceConfigurationAck.Create(Device, Tenant);

        ack.Applied(3, Now);
        ack.Rejected(4, new string('e', 600), Now.AddMinutes(1));

        ack.AppliedVersion.ShouldBe(3);
        ack.RejectedVersion.ShouldBe(4);
        ack.Error!.Length.ShouldBe(500);
        ack.Applied(2, Now.AddMinutes(2));
        ack.AppliedVersion.ShouldBe(3, "an older answer does not move the version back");
        ack.RejectedVersion.ShouldBeNull();
        ack.DomainEvents.OfType<DeviceConfigurationAppliedV1>().Select(e => e.Success).ShouldBe([true, false, true]);
    }

    [Fact]
    public void Tenant_defaults_can_be_changed()
    {
        var defaults = TenantConfigurationDefaults.Create(Tenant, "{}", Now);
        defaults.Update("{\"b\":2}", Now.AddHours(1));

        defaults.DocumentJson.ShouldBe("{\"b\":2}");
        defaults.UpdatedAt.ShouldBe(Now.AddHours(1));
    }

    [Theory]
    [InlineData("Website", "https://shop.example.test", true)]
    [InlineData("Website", "ftp://shop.example.test", false)]
    [InlineData("Ping", "198.51.100.1", true)]
    [InlineData("Ping", "198.51.100.1/24", false)]
    [InlineData("Disk", "C:", true)]
    [InlineData("Service", "W3SVC", true)]
    [InlineData("Teleport", "x", false)]
    public void Monitor_point_targets_are_validated_per_type(string type, string target, bool valid)
    {
        var create = () => MonitorPoint.FromCloud(Tenant, Device, "p1", "Point", type, target, 60, "Problem", true, true, null, 0);

        if (valid)
            create().Origin.ShouldBe(MonitorPoint.CloudOrigin);
        else
            Should.Throw<DomainException>(create);
    }

    [Fact]
    public void Editing_an_agent_point_hands_it_to_the_cloud()
    {
        var point = MonitorPoint.FromAgent(Tenant, Device, "web", "Web", "Website", "https://web.example.test", true, 60, 0);
        point.Origin.ShouldBe(MonitorPoint.AgentOrigin);

        point.Edit("Web (cloud)", "Website", "https://web.example.test/health", 30, "Warning", false, false, "{\"expectStatus\":204}");

        point.Origin.ShouldBe(MonitorPoint.CloudOrigin);
        point.AlertLevel.ShouldBe("Warning");
        point.SettingsJson.ShouldBe("{\"expectStatus\":204}");
        Should.Throw<DomainException>(() => point.Edit("x", "Website", "https://x.test", 1, "Problem", true, true, null));
        Should.Throw<DomainException>(() => point.Edit("x", "Website", "https://x.test", 60, "Loud", true, true, null));
    }

    [Fact]
    public void The_document_rules_reject_inconsistent_levels()
    {
        ConfigDocuments.Validate(ConfigDocuments.Default).ShouldBeEmpty();
        var bad = ConfigDocuments.Default with
        {
            Telemetry = new TelemetrySettings(0),
            Thresholds = ConfigDocuments.Default.Thresholds with
            {
                Cpu = new UsageThreshold(90, 80, 4000, 95),
                TempC = new TemperatureThreshold(10, -1),
            },
        };

        var errors = ConfigDocuments.Validate(bad);

        errors.Keys.ShouldBe(["telemetry.sampleSeconds", "thresholds.cpu.criticalPercent", "thresholds.cpu.forSeconds", "thresholds.cpu.clearBelowPercent", "thresholds.tempC.critical", "thresholds.tempC.forSeconds"], ignoreOrder: true);
        ConfigDocuments.Validate(null).Keys.ShouldBe(["document"]);
        ConfigDocuments.TryParse("not json").ShouldBeNull();
    }
}
