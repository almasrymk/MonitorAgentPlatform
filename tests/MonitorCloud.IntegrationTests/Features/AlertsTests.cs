using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Monitoring;
using MonitorCloud.Application.Notifications;
using MonitorCloud.Application.Tenancy;
using MonitorCloud.Domain.Audit;
using MonitorCloud.Domain.Monitoring;
using MonitorCloud.Domain.Notifications;
using MonitorCloud.Infrastructure.Persistence;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Features;

/// <summary>Alerts, notifications and alert settings endpoints (MC-604).</summary>
[Collection(SqlCollection.Name)]
public sealed class AlertsTests(SqlServerFixture sql) : FeatureTestBase(sql)
{
    private Task<Alert> AddAlertAsync(Guid tenantId, Guid locationId, Guid deviceId, string key, AlertSeverity severity) => App.InSystemScopeAsync(async sp =>
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var alert = Alert.Raise(tenantId, locationId, deviceId, key, AlertCategories.Performance, severity, $"Alert {key}", "message", AlertSource.Agent, App.Clock.GetUtcNow(), false);
        alert.ClearDomainEvents();
        db.Add(alert);
        await db.SaveChangesAsync();
        return alert;
    });

    private Task<Notification> AddNotificationAsync(Guid? tenantId, Guid? locationId, string title) => App.InSystemScopeAsync(async sp =>
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var notification = Notification.Create(tenantId, "Warning", "Performance", title, "body", locationId, null, null, App.Clock.GetUtcNow());
        db.Add(notification);
        await db.SaveChangesAsync();
        return notification;
    });

    [Fact]
    public async Task Alerts_are_listed_newest_first_with_filters_and_device_names()
    {
        var critical = await AddAlertAsync(World.A.Id, World.A.Location2.Id, World.A.Device2.Id, "cpu", AlertSeverity.Critical);
        using var client = App.ClientFor(World.A.ReportViewer);

        var all = await (await client.GetAsync(new Uri("/api/v1/alerts", UriKind.Relative))).ShouldBeOkAsync<PagedResult<AlertDto>>();
        all.Total.ShouldBe(2);
        var critOnly = await (await client.GetAsync(new Uri("/api/v1/alerts?severity=critical", UriKind.Relative))).ShouldBeOkAsync<PagedResult<AlertDto>>();
        critOnly.Items.ShouldHaveSingleItem().Id.ShouldBe(critical.Id);
        critOnly.Items[0].DeviceName.ShouldBe(World.A.Device2.Name);
        critOnly.Items[0].LocationName.ShouldBe(World.A.Location2.Name);
        var bySeverity = await (await client.GetAsync(new Uri("/api/v1/alerts?sort=severity", UriKind.Relative))).ShouldBeOkAsync<PagedResult<AlertDto>>();
        bySeverity.Items[0].Severity.ShouldBe("Critical");
        var atLocation = await (await client.GetAsync(new Uri($"/api/v1/alerts?locationId={World.A.Location1.Id}", UriKind.Relative))).ShouldBeOkAsync<PagedResult<AlertDto>>();
        atLocation.Items.ShouldHaveSingleItem().Id.ShouldBe(World.A.Alert1.Id);
        await (await client.GetAsync(new Uri("/api/v1/alerts?severity=fatal", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        var single = await (await client.GetAsync(new Uri($"/api/v1/alerts/{critical.Id}", UriKind.Relative))).ShouldBeOkAsync<AlertDto>();
        single.IssueKey.ShouldBe("cpu");
    }

    [Fact]
    public async Task A_location_restricted_user_sees_only_alerts_of_their_locations()
    {
        var other = await AddAlertAsync(World.A.Id, World.A.Location2.Id, World.A.Device2.Id, "cpu", AlertSeverity.Critical);
        using var client = App.ClientFor(World.A.RestrictedManager);

        var page = await (await client.GetAsync(new Uri("/api/v1/alerts", UriKind.Relative))).ShouldBeOkAsync<PagedResult<AlertDto>>();

        page.Items.ShouldHaveSingleItem().Id.ShouldBe(World.A.Alert1.Id);
        await (await client.GetAsync(new Uri($"/api/v1/alerts/{other.Id}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "ALERT_NOT_FOUND");
    }

    [Fact]
    public async Task Acknowledge_and_resolve_are_audited_and_resolved_alerts_cannot_change()
    {
        using var client = App.ClientFor(World.A.Technician);
        var id = World.A.Alert1.Id;

        (await client.PostAsync(new Uri($"/api/v1/alerts/{id}/acknowledge", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PostAsync(new Uri($"/api/v1/alerts/{id}/resolve", UriKind.Relative), null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var alert = await (await client.GetAsync(new Uri($"/api/v1/alerts/{id}", UriKind.Relative))).ShouldBeOkAsync<AlertDto>();
        alert.Status.ShouldBe("Resolved");
        alert.ResolvedBy.ShouldBe("User");
        alert.AcknowledgedAt.ShouldNotBeNull();
        await (await client.PostAsync(new Uri($"/api/v1/alerts/{id}/resolve", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "ALERT_RESOLVED");
        await (await client.PostAsync(new Uri($"/api/v1/alerts/{id}/acknowledge", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "ALERT_RESOLVED");
        var actions = await App.InDbAsync(db => db.Set<AuditRecord>().Where(a => a.EntityId == id.ToString()).Select(a => a.Action).ToListAsync());
        actions.ShouldBe(["alert.acknowledged", "alert.resolved"], ignoreOrder: true);
        (await (await client.GetAsync(new Uri($"/api/v1/devices/{World.A.Device1.Id}/alerts?status=all", UriKind.Relative))).ShouldBeOkAsync<PagedResult<AlertDto>>())
            .Items.ShouldContain(a => a.Id == id);
    }

    [Fact]
    public async Task Self_resolving_alerts_cannot_be_resolved_by_a_user()
    {
        var offline = await AddAlertAsync(World.A.Id, World.A.Location1.Id, World.A.Device1.Id, CloudIssues.DeviceOffline, AlertSeverity.Critical);
        using var client = App.ClientFor(World.A.Administrator);

        await (await client.PostAsync(new Uri($"/api/v1/alerts/{offline.Id}/resolve", UriKind.Relative), null)).ShouldBeProblemAsync(HttpStatusCode.Conflict, "ALERT_SELF_RESOLVING");
        (await (await client.GetAsync(new Uri($"/api/v1/alerts/{offline.Id}", UriKind.Relative))).ShouldBeOkAsync<AlertDto>()).CanResolve.ShouldBeFalse();
    }

    [Fact]
    public async Task Device_alerts_of_an_unknown_device_are_not_found()
    {
        using var client = App.ClientFor(World.A.Administrator);

        await (await client.GetAsync(new Uri($"/api/v1/devices/{Guid.CreateVersion7()}/alerts", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "DEVICE_NOT_FOUND");
        await (await client.GetAsync(new Uri($"/api/v1/devices/{Guid.CreateVersion7()}/monitor-points", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "DEVICE_NOT_FOUND");
        (await (await client.GetAsync(new Uri($"/api/v1/devices/{World.A.Device1.Id}/monitor-points", UriKind.Relative))).ShouldBeOkAsync<List<MonitorPointDto>>()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Platform_alerts_cover_every_customer_with_names()
    {
        await AddAlertAsync(World.B.Id, World.B.Location1.Id, World.B.Device1.Id, "cpu", AlertSeverity.Critical);
        using var client = App.ClientFor(World.PlatformSupport);

        var page = await (await client.GetAsync(new Uri("/api/v1/platform/alerts", UriKind.Relative))).ShouldBeOkAsync<PagedResult<PlatformAlertDto>>();
        page.Total.ShouldBe(3);
        page.Items.Select(a => a.CustomerName).Distinct().ShouldBe([World.A.Tenant.Name, World.B.Tenant.Name], ignoreOrder: true);
        var onlyB = await (await client.GetAsync(new Uri($"/api/v1/platform/alerts?tenantId={World.B.Id}", UriKind.Relative))).ShouldBeOkAsync<PagedResult<PlatformAlertDto>>();
        onlyB.Items.ShouldAllBe(a => a.TenantId == World.B.Id);
        onlyB.Items.ShouldContain(a => a.Severity == "Critical" && a.DeviceName == World.B.Device1.Name);
    }

    // ---------------------------------------------------------------- notifications

    [Fact]
    public async Task Notifications_are_read_one_by_one_or_all_and_the_unread_count_follows()
    {
        var first = await AddNotificationAsync(World.A.Id, World.A.Location1.Id, "first");
        await AddNotificationAsync(World.A.Id, World.A.Location2.Id, "second");
        await AddNotificationAsync(World.B.Id, null, "other tenant");
        using var client = App.ClientFor(World.A.Technician);

        async Task<int> UnreadAsync() => (await (await client.GetAsync(new Uri("/api/v1/notifications/unread-count", UriKind.Relative))).ShouldBeOkAsync<UnreadCountDto>()).Unread;

        (await UnreadAsync()).ShouldBe(2);
        (await client.PostJsonAsync("/api/v1/notifications/read", new { ids = new[] { first.Id } })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await UnreadAsync()).ShouldBe(1);
        var page = await (await client.GetAsync(new Uri("/api/v1/notifications", UriKind.Relative))).ShouldBeOkAsync<PagedResult<NotificationDto>>();
        page.Items.Count.ShouldBe(2);
        page.Items.Single(n => n.Id == first.Id).Read.ShouldBeTrue();
        (await client.PostJsonAsync("/api/v1/notifications/read", new { all = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await UnreadAsync()).ShouldBe(0);
        await (await client.PostJsonAsync("/api/v1/notifications/read", new { })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        // Read state is per user.
        using var other = App.ClientFor(World.A.ReportViewer);
        (await (await other.GetAsync(new Uri("/api/v1/notifications/unread-count", UriKind.Relative))).ShouldBeOkAsync<UnreadCountDto>()).Unread.ShouldBe(2);
        using var restricted = App.ClientFor(World.A.RestrictedManager);
        var scoped = await (await restricted.GetAsync(new Uri("/api/v1/notifications", UriKind.Relative))).ShouldBeOkAsync<PagedResult<NotificationDto>>();
        scoped.Items.ShouldHaveSingleItem().Title.ShouldBe("first");
    }

    [Fact]
    public async Task The_platform_feed_holds_platform_notifications_only()
    {
        var platform = await AddNotificationAsync(null, null, "platform");
        await AddNotificationAsync(World.A.Id, null, "tenant");
        using var client = App.ClientFor(World.PlatformAdmin);

        var page = await (await client.GetAsync(new Uri("/api/v1/platform/notifications", UriKind.Relative))).ShouldBeOkAsync<PagedResult<NotificationDto>>();
        page.Items.ShouldHaveSingleItem().Id.ShouldBe(platform.Id);
        (await (await client.GetAsync(new Uri("/api/v1/platform/notifications/unread-count", UriKind.Relative))).ShouldBeOkAsync<UnreadCountDto>()).Unread.ShouldBe(1);
        (await client.PostJsonAsync("/api/v1/platform/notifications/read", new { all = true })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await (await client.GetAsync(new Uri("/api/v1/platform/notifications/unread-count", UriKind.Relative))).ShouldBeOkAsync<UnreadCountDto>()).Unread.ShouldBe(0);

        // A tenant user never sees platform rows.
        using var tenant = App.ClientFor(World.A.Administrator);
        (await (await tenant.GetAsync(new Uri("/api/v1/notifications", UriKind.Relative))).ShouldBeOkAsync<PagedResult<NotificationDto>>())
            .Items.ShouldAllBe(n => n.TenantId == World.A.Id);
    }

    // ---------------------------------------------------------------- settings

    [Fact]
    public async Task General_settings_change_time_zone_language_and_offline_alert()
    {
        using var client = App.ClientFor(World.A.Administrator);

        var defaults = await (await client.GetAsync(new Uri("/api/v1/settings/general", UriKind.Relative))).ShouldBeOkAsync<GeneralSettingsDto>();
        defaults.ShouldBe(new GeneralSettingsDto("Africa/Cairo", "en", "Critical", 2));
        var updated = await (await client.PutJsonAsync("/api/v1/settings/general",
            new { timeZone = "Asia/Dubai", defaultLanguage = "ar", offlineAlertSeverity = "Warning", offlineAlertDelayMinutes = 10 })).ShouldBeOkAsync<GeneralSettingsDto>();
        updated.ShouldBe(new GeneralSettingsDto("Asia/Dubai", "ar", "Warning", 10));
        (await (await client.GetAsync(new Uri("/api/v1/settings/general", UriKind.Relative))).ShouldBeOkAsync<GeneralSettingsDto>()).ShouldBe(updated);
        await (await client.PutJsonAsync("/api/v1/settings/general", new { timeZone = "Mars/Base", defaultLanguage = "fr", offlineAlertSeverity = "Fatal", offlineAlertDelayMinutes = 0 }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        (await App.InDbAsync(db => db.Set<AuditRecord>().AnyAsync(a => a.Action == "settings.general_updated"))).ShouldBeTrue();
    }

    [Fact]
    public async Task Alert_channels_default_on_and_webhooks_need_the_plan_feature()
    {
        using var a = App.ClientFor(World.A.Administrator);
        var defaults = await (await a.GetAsync(new Uri("/api/v1/settings/alerts", UriKind.Relative))).ShouldBeOkAsync<AlertSettingsDto>();
        defaults.EmailEnabled.ShouldBeTrue();
        defaults.InAppEnabled.ShouldBeTrue();
        defaults.SmsAvailable.ShouldBeFalse();
        defaults.WebhookEntitled.ShouldBeTrue();

        (await a.PutJsonAsync("/api/v1/settings/alerts", new { emailEnabled = false, inAppEnabled = true, webhookEnabled = true, webhookUrl = "https://hooks.alpha.test/monitor" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var saved = await (await a.GetAsync(new Uri("/api/v1/settings/alerts", UriKind.Relative))).ShouldBeOkAsync<AlertSettingsDto>();
        saved.EmailEnabled.ShouldBeFalse();
        saved.WebhookUrl.ShouldBe("https://hooks.alpha.test/monitor");
        await (await a.PutJsonAsync("/api/v1/settings/alerts", new { webhookEnabled = true, webhookUrl = "http://hooks.alpha.test" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        // Starter (tenant B) has no webhooks.
        using var b = App.ClientFor(World.B.Administrator);
        await (await b.PutJsonAsync("/api/v1/settings/alerts", new { webhookEnabled = true, webhookUrl = "https://hooks.beta.test/monitor" }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FEATURE_NOT_ENTITLED");
    }

    [Fact]
    public async Task Recipients_are_added_updated_and_removed_with_unique_e_mails()
    {
        using var client = App.ClientFor(World.A.Administrator);

        var created = await (await client.PostJsonAsync("/api/v1/settings/alerts/recipients",
            new { name = "Night shift", email = "Night@Alpha.test", events = "CriticalOnly", locationId = World.A.Location1.Id })).ShouldBeOkAsync<RecipientDto>(HttpStatusCode.Created);
        created.Email.ShouldBe("night@alpha.test");
        await (await client.PostJsonAsync("/api/v1/settings/alerts/recipients", new { name = "Again", email = "night@alpha.test", events = "All" }))
            .ShouldBeProblemAsync(HttpStatusCode.Conflict, "RECIPIENT_EMAIL_TAKEN");
        await (await client.PostJsonAsync("/api/v1/settings/alerts/recipients", new { name = "Far", email = "far@alpha.test", events = "All", locationId = World.B.Location1.Id }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "LOCATION_NOT_FOUND");
        await (await client.PostJsonAsync("/api/v1/settings/alerts/recipients", new { name = "Bad", email = "not-an-email", events = "Sometimes" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        var updated = await (await client.PutJsonAsync($"/api/v1/settings/alerts/recipients/{created.Id}",
            new { name = "Night shift", email = "night@alpha.test", events = "WarningsAndCritical", isActive = false })).ShouldBeOkAsync<RecipientDto>();
        updated.Events.ShouldBe("WarningsAndCritical");
        updated.IsActive.ShouldBeFalse();
        var list = await (await client.GetAsync(new Uri("/api/v1/settings/alerts/recipients", UriKind.Relative))).ShouldBeOkAsync<List<RecipientDto>>();
        list.Select(r => r.Email).ShouldBe(["night@alpha.test", World.A.Recipient1.Email], ignoreOrder: true);
        (await client.DeleteAsync(new Uri($"/api/v1/settings/alerts/recipients/{created.Id}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await (await client.DeleteAsync(new Uri($"/api/v1/settings/alerts/recipients/{created.Id}", UriKind.Relative))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "RECIPIENT_NOT_FOUND");
    }

    [Fact]
    public async Task Failed_e_mail_deliveries_are_retried_and_then_marked_failed()
    {
        var delivery = await App.InSystemScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var d = NotificationDelivery.Email(World.A.Id, null, "broken@alpha.test", "Subject", "Body", App.Clock.GetUtcNow());
            db.Add(d);
            await db.SaveChangesAsync();
            return d;
        });
        App.Mail.FailNext = 10;

        for (var i = 0; i < 6; i++)
        {
            await App.InSystemScopeAsync(sp => sp.GetRequiredService<NotificationDeliveryService>().RunAsync(CancellationToken.None));
            App.Clock.Advance(TimeSpan.FromMinutes(10));
        }

        var row = await App.InDbAsync(db => db.Set<NotificationDelivery>().AsNoTracking().SingleAsync(d => d.Id == delivery.Id));
        row.Status.ShouldBe(DeliveryStatus.Failed);
        row.Attempts.ShouldBe(NotificationDelivery.MaxAttempts);
        row.LastError.ShouldNotBeNull();
        App.Mail.FailNext = 0;
    }
}
