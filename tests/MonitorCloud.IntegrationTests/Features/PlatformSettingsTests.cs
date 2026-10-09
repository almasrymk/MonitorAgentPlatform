using System.Net;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Tenancy;
using MonitorCloud.Domain.Audit;
using MonitorCloud.TestShared;

namespace MonitorCloud.IntegrationTests.Features;

/// <summary>Platform Settings (06: /platform/settings) and their effect on customers without their own settings.</summary>
[Collection(SqlCollection.Name)]
public sealed class PlatformSettingsTests(SqlServerFixture sql) : FeatureTestBase(sql)
{
    private static object Body(int delay = 5, int minuteDays = 14, int hourDays = 365) => new
    {
        brandName = "Monitor Test",
        offlineAlertDelayMinutes = delay,
        minuteRetentionDays = minuteDays,
        hourRetentionDays = hourDays,
        emailSenderName = "Monitor Test",
        emailSenderAddress = "no-reply@monitor.local",
    };

    [Fact]
    public async Task Defaults_are_returned_before_the_first_save_and_saving_is_audited()
    {
        using var admin = App.ClientFor(World.PlatformAdmin);

        var defaults = await (await admin.GetAsync(new Uri("/api/v1/platform/settings", UriKind.Relative))).ShouldBeOkAsync<PlatformSettingsDto>();
        (defaults.OfflineAlertDelayMinutes, defaults.MinuteRetentionDays, defaults.HourRetentionDays).ShouldBe((2, 30, 400));

        var saved = await (await admin.PutJsonAsync("/api/v1/platform/settings", Body())).ShouldBeOkAsync<PlatformSettingsDto>();
        (saved.BrandName, saved.OfflineAlertDelayMinutes, saved.MinuteRetentionDays).ShouldBe(("Monitor Test", 5, 14));
        (await App.InDbAsync(db => db.Set<AuditRecord>().IgnoreQueryFilters().AnyAsync(a => a.Action == "platform.settings_updated"))).ShouldBeTrue();
    }

    [Fact]
    public async Task Values_outside_their_ranges_are_rejected()
    {
        using var admin = App.ClientFor(World.PlatformAdmin);

        await (await admin.PutJsonAsync("/api/v1/platform/settings", Body(delay: 0))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await admin.PutJsonAsync("/api/v1/platform/settings", Body(minuteDays: 3))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        await (await admin.PutJsonAsync("/api/v1/platform/settings", Body(hourDays: 5000))).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Only_platform_admins_manage_them()
    {
        using var support = App.ClientFor(World.PlatformSupport);
        using var tenant = App.ClientFor(World.A.Administrator);

        (await support.GetAsync(new Uri("/api/v1/platform/settings", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await tenant.PutJsonAsync("/api/v1/platform/settings", Body())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_platform_offline_delay_applies_to_customers_without_their_own()
    {
        using var admin = App.ClientFor(World.PlatformAdmin);
        (await admin.PutJsonAsync("/api/v1/platform/settings", Body(delay: 7))).EnsureSuccessStatusCode();
        using var a = App.ClientFor(World.A.Administrator);

        (await (await a.GetAsync(new Uri("/api/v1/settings/general", UriKind.Relative))).ShouldBeOkAsync<GeneralSettingsDto>()).OfflineAlertDelayMinutes.ShouldBe(7);

        (await a.PutJsonAsync("/api/v1/settings/general", new { timeZone = "Africa/Cairo", defaultLanguage = "en", offlineAlertSeverity = "Warning", offlineAlertDelayMinutes = 3 })).EnsureSuccessStatusCode();
        (await (await a.GetAsync(new Uri("/api/v1/settings/general", UriKind.Relative))).ShouldBeOkAsync<GeneralSettingsDto>()).OfflineAlertDelayMinutes.ShouldBe(3, "the customer's own value wins");
    }
}
