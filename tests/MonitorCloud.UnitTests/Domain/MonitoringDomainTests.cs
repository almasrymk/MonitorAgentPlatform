using MonitorCloud.Domain.Monitoring;
using MonitorCloud.Domain.Notifications;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared;

namespace MonitorCloud.UnitTests.Domain;

public sealed class AlertTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly Guid Location = Guid.CreateVersion7();
    private static readonly Guid Device = Guid.CreateVersion7();

    private static Alert Raise(string key = "cpu", AlertSeverity severity = AlertSeverity.Warning, bool notify = true) =>
        Alert.Raise(Tenant, Location, Device, key, "performance", severity, "High CPU usage", "CPU above 90 %", AlertSource.Agent, Now, notify);

    [Fact]
    public void Raise_opens_the_alert_once_and_raises_AlertRaisedV1()
    {
        var alert = Raise();

        alert.IsOpen.ShouldBeTrue();
        alert.Occurrences.ShouldBe(1);
        alert.Category.ShouldBe(AlertCategories.Performance);
        alert.FirstSeenAt.ShouldBe(Now);
        var raised = alert.DomainEvents.OfType<AlertRaisedV1>().Single();
        raised.Notify.ShouldBeTrue();
        raised.Severity.ShouldBe("Warning");
        raised.Category.ShouldBe(AlertCategories.Performance);
    }

    [Fact]
    public void Unknown_categories_become_System_and_long_texts_are_cut()
    {
        var alert = Alert.Raise(Tenant, Location, Device, "x", "Weird", AlertSeverity.Info, new string('t', 300), new string('m', 3000), AlertSource.Agent, Now, false);

        alert.Category.ShouldBe(AlertCategories.System);
        alert.Title.Length.ShouldBe(Alert.TitleMax);
        alert.Message.Length.ShouldBe(Alert.MessageMax);
        alert.DomainEvents.OfType<AlertRaisedV1>().Single().Notify.ShouldBeFalse();
    }

    [Fact]
    public void Touch_counts_occurrences_and_only_a_new_severity_raises_an_event()
    {
        var alert = Raise();
        alert.ClearDomainEvents();

        alert.Touch(AlertSeverity.Warning, null, null, Now.AddMinutes(1));
        alert.DomainEvents.ShouldBeEmpty();
        alert.Touch(AlertSeverity.Critical, "Very high CPU", "CPU 99 %", Now.AddMinutes(2));

        alert.Occurrences.ShouldBe(3);
        alert.LastSeenAt.ShouldBe(Now.AddMinutes(2));
        alert.Severity.ShouldBe(AlertSeverity.Critical);
        alert.Title.ShouldBe("Very high CPU");
        var changed = alert.DomainEvents.OfType<AlertSeverityChangedV1>().Single();
        changed.From.ShouldBe("Warning");
        changed.To.ShouldBe("Critical");
    }

    [Fact]
    public void Touch_with_an_older_time_keeps_the_last_seen_time()
    {
        var alert = Raise();

        alert.Touch(AlertSeverity.Warning, null, null, Now.AddMinutes(-5));

        alert.LastSeenAt.ShouldBe(Now);
    }

    [Fact]
    public void Acknowledge_keeps_the_first_acknowledgement()
    {
        var alert = Raise();
        var first = Guid.CreateVersion7();

        alert.Acknowledge(first, Now.AddMinutes(1));
        alert.Acknowledge(Guid.CreateVersion7(), Now.AddMinutes(2));

        alert.AcknowledgedByUserId.ShouldBe(first);
        alert.AcknowledgedAt.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public void Resolve_closes_once_and_rejects_later_changes()
    {
        var alert = Raise();
        alert.ClearDomainEvents();

        alert.Resolve(ResolvedBy.User, Now.AddMinutes(3));
        alert.Resolve(ResolvedBy.User, Now.AddMinutes(4));

        alert.Status.ShouldBe(AlertStatus.Resolved);
        alert.ResolvedAt.ShouldBe(Now.AddMinutes(3));
        alert.ResolvedBy.ShouldBe(ResolvedBy.User);
        alert.DomainEvents.OfType<AlertResolvedV1>().Count().ShouldBe(1);
        Should.Throw<DomainException>(() => alert.Touch(AlertSeverity.Warning, null, null, Now)).Error.Code.ShouldBe("ALERT_RESOLVED");
        Should.Throw<DomainException>(() => alert.Acknowledge(Guid.CreateVersion7(), Now)).Error.Code.ShouldBe("ALERT_RESOLVED");
    }

    [Theory]
    [InlineData(CloudIssues.DeviceOffline)]
    [InlineData(CloudIssues.License)]
    public void Self_resolving_alerts_cannot_be_resolved_by_a_user(string key)
    {
        var alert = Raise(key);

        Should.Throw<DomainException>(() => alert.Resolve(ResolvedBy.User, Now)).Error.Code.ShouldBe("ALERT_SELF_RESOLVING");
        alert.Resolve(ResolvedBy.Auto, Now);
        alert.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void A_condition_raised_again_after_resolve_is_a_new_alert()
    {
        var first = Raise();
        first.Resolve(ResolvedBy.Auto, Now);

        var second = Raise();

        second.Id.ShouldNotBe(first.Id);
        second.Occurrences.ShouldBe(1);
    }

    [Fact]
    public void MoveTo_and_SeedResolved_change_location_and_status()
    {
        var alert = Raise();
        var other = Guid.CreateVersion7();

        alert.MoveTo(other);
        alert.SeedResolved(Now.AddHours(1));

        alert.LocationId.ShouldBe(other);
        alert.Status.ShouldBe(AlertStatus.Resolved);
        alert.LastSeenAt.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void Daily_stats_count_by_severity()
    {
        var stat = AlertDailyStat.Create(Tenant, Location, DateOnly.FromDateTime(Now.UtcDateTime));

        stat.Count(AlertSeverity.Critical);
        stat.Count(AlertSeverity.Warning, 2);
        stat.Count(AlertSeverity.Info);

        (stat.Critical, stat.Warning, stat.Info).ShouldBe((1, 2, 1));
    }

    [Fact]
    public void Monitoring_settings_validate_the_offline_delay()
    {
        var settings = MonitoringSettings.Default(Tenant);
        settings.OfflineSeverity.ShouldBe(AlertSeverity.Critical);
        settings.OfflineDelayMinutes.ShouldBe(2);

        settings.Update(AlertSeverity.Warning, 5);
        settings.OfflineDelayMinutes.ShouldBe(5);
        Should.Throw<DomainException>(() => settings.Update(AlertSeverity.Warning, 0));
        Should.Throw<DomainException>(() => settings.Update(AlertSeverity.Warning, 61));
    }

    [Fact]
    public void Monitor_points_are_imported_and_updated_from_the_agent()
    {
        var point = MonitorPoint.FromAgent(Tenant, Device, "web", "", "", "https://shop.example.test", true, 60, 0);
        point.DisplayName.ShouldBe("web");
        point.Type.ShouldBe("Custom");
        point.Origin.ShouldBe("Agent");

        point.UpdateFromAgent("Shop", "Website", "https://shop.example.test/health", false, -5);
        point.DisplayName.ShouldBe("Shop");
        point.Enabled.ShouldBeFalse();
        point.IntervalSeconds.ShouldBe(0);

        var state = MonitorPointState.Create(point.Id, Tenant, Device);
        state.Status.ShouldBe(PointStatus.Unknown);
        state.Update(PointStatus.Critical, new string('x', 600), 120.5m, Now, Now);
        state.Message.Length.ShouldBe(500);
        state.ResponseMs.ShouldBe(120.5m);
    }

    [Fact]
    public void Pending_offline_alert_keeps_device_and_reason()
    {
        var pending = PendingOfflineAlert.Create(Device, Tenant, Location, new string('r', 40), Now);

        pending.Id.ShouldBe(Device);
        pending.Reason.Length.ShouldBe(32);
        pending.WentOfflineAt.ShouldBe(Now);
    }
}

public sealed class NotificationDomainTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;
    private static readonly Guid Tenant = Guid.CreateVersion7();

    [Theory]
    [InlineData(RecipientEvents.All, 1, true)]
    [InlineData(RecipientEvents.CriticalOnly, 2, false)]
    [InlineData(RecipientEvents.CriticalOnly, 3, true)]
    [InlineData(RecipientEvents.WarningsAndCritical, 1, false)]
    [InlineData(RecipientEvents.WarningsAndCritical, 2, true)]
    public void Recipients_filter_by_severity(RecipientEvents events, byte severity, bool expected)
    {
        var recipient = AlertRecipient.Create(Tenant, "Ops", "OPS@acme.test", events, null);

        recipient.Email.ShouldBe("ops@acme.test");
        recipient.Wants(severity, Guid.CreateVersion7()).ShouldBe(expected);
    }

    [Fact]
    public void Recipients_of_one_location_and_inactive_recipients_are_skipped()
    {
        var location = Guid.CreateVersion7();
        var recipient = AlertRecipient.Create(Tenant, "Cairo", "cairo@acme.test", RecipientEvents.All, location);

        recipient.Wants(3, location).ShouldBeTrue();
        recipient.Wants(3, Guid.CreateVersion7()).ShouldBeFalse();
        recipient.Update("Cairo", "cairo@acme.test", RecipientEvents.All, location, isActive: false);
        recipient.Wants(3, location).ShouldBeFalse();
    }

    [Fact]
    public void Channel_settings_default_to_email_and_in_app_and_need_https_webhooks()
    {
        var settings = AlertChannelSettings.Default(Tenant, Now);
        settings.EmailEnabled.ShouldBeTrue();
        settings.InAppEnabled.ShouldBeTrue();
        settings.SmsEnabled.ShouldBeFalse();

        Should.Throw<DomainException>(() => settings.Update(true, true, true, "http://hooks.example.test/x", Now));
        Should.Throw<DomainException>(() => settings.Update(true, true, true, null, Now));
        settings.Update(false, true, true, "https://hooks.example.test/x", Now);
        settings.WebhookUrl.ShouldBe("https://hooks.example.test/x");
        settings.Update(false, true, false, "https://hooks.example.test/x", Now);
        settings.WebhookUrl.ShouldBeNull();
    }

    [Fact]
    public void Notifications_cut_long_texts()
    {
        var notification = Notification.Create(null, "Warning", "System", new string('t', 250), new string('b', 1200), null, null, null, Now);

        notification.TenantId.ShouldBeNull();
        notification.Title.Length.ShouldBe(200);
        notification.Body.Length.ShouldBe(1000);
        NotificationRead.Create(notification.Id, Guid.CreateVersion7(), Now).NotificationId.ShouldBe(notification.Id);
    }

    [Fact]
    public void Deliveries_retry_with_back_off_then_fail()
    {
        var delivery = NotificationDelivery.Email(Tenant, null, "ops@acme.test", "Subject", "Body", Now);
        delivery.Status.ShouldBe(DeliveryStatus.Pending);

        delivery.Failed("smtp down", Now);
        delivery.NextAttemptAt.ShouldBe(Now.AddMinutes(1));
        delivery.Failed("smtp down", Now);
        delivery.NextAttemptAt.ShouldBe(Now.AddMinutes(2));
        delivery.Failed("smtp down", Now);
        delivery.Failed("smtp down", Now);
        delivery.Status.ShouldBe(DeliveryStatus.Pending);
        delivery.Failed(new string('e', 600), Now);

        delivery.Status.ShouldBe(DeliveryStatus.Failed);
        delivery.Attempts.ShouldBe(NotificationDelivery.MaxAttempts);
        delivery.LastError!.Length.ShouldBe(500);
    }

    [Fact]
    public void A_sent_delivery_records_the_time()
    {
        var delivery = NotificationDelivery.Email(Tenant, Guid.CreateVersion7(), "ops@acme.test", "S", "B", Now);

        delivery.Sent(Now.AddSeconds(5));

        delivery.Status.ShouldBe(DeliveryStatus.Sent);
        delivery.SentAt.ShouldBe(Now.AddSeconds(5));
        delivery.Attempts.ShouldBe(1);
    }
}
