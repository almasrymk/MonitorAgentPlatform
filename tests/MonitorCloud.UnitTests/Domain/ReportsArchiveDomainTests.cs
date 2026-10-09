using MonitorCloud.Domain.Archive;
using MonitorCloud.Domain.Media;
using MonitorCloud.Domain.Reports;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared;

namespace MonitorCloud.UnitTests.Domain;

public sealed class ReportsArchiveDomainTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly Guid User = Guid.CreateVersion7();

    [Theory]
    [InlineData("contract.pdf", "contract.pdf")]
    [InlineData(@"..\..\windows\system.ini.txt", "system.ini.txt")]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData(@"C:\Users\x\report.csv", "report.csv")]
    [InlineData("c:report.csv", "report.csv")]
    [InlineData("  spaced name.png ", "spaced name.png")]
    [InlineData("a<b>|c?.png", "abc.png")]
    public void Safe_names_keep_only_the_last_segment(string input, string expected) =>
        MediaFile.SafeName(input).ShouldBe(expected);

    [Theory]
    [InlineData("..")]
    [InlineData("../..")]
    [InlineData("...")]
    [InlineData(@"folder\")]
    public void Names_without_a_file_are_refused(string input) =>
        Should.Throw<DomainException>(() => MediaFile.SafeName(input)).Error.Code.ShouldBe("UPLOAD_NAME_INVALID");

    [Fact]
    public void A_media_file_takes_its_content_type_from_the_allow_list_and_lives_under_its_tenant()
    {
        var file = MediaFile.Create(Tenant, "../Scan.PNG", 10, new string('a', 64), Now);

        file.FileName.ShouldBe("Scan.PNG");
        file.ContentType.ShouldBe("image/png");
        file.StoragePath.ShouldBe($"{Tenant:N}/{file.Id:N}");
        MediaFile.Create(null, "r.csv", 1, new string('a', 64), Now).StoragePath.ShouldStartWith("platform/");
    }

    [Theory]
    [InlineData("x.exe", 10, "UPLOAD_TYPE_NOT_ALLOWED", ErrorKind.UnsupportedMediaType)]
    [InlineData("x.svg", 10, "UPLOAD_TYPE_NOT_ALLOWED", ErrorKind.UnsupportedMediaType)]
    [InlineData("x.pdf", 0, "UPLOAD_EMPTY", ErrorKind.Validation)]
    [InlineData("x.pdf", MediaFile.MaxBytes + 1, "UPLOAD_TOO_LARGE", ErrorKind.PayloadTooLarge)]
    public void Invalid_files_are_refused(string name, long size, string code, ErrorKind kind)
    {
        var error = Should.Throw<DomainException>(() => MediaFile.Create(Tenant, name, size, new string('a', 64), Now)).Error;

        (error.Code, error.Kind).ShouldBe((code, kind));
    }

    [Fact]
    public void A_report_goes_from_queued_to_done_or_failed()
    {
        var report = GeneratedReport.Request(Tenant, "overview", "Overview", "{}", ReportFormat.Csv, User, Now);
        report.Status.ShouldBe(ReportStatus.Queued);

        report.Start();
        report.Status.ShouldBe(ReportStatus.Running);
        var media = Guid.CreateVersion7();
        report.Complete(media, 120, Now.AddSeconds(3));
        (report.Status, report.MediaId, report.SizeBytes, report.CompletedAt).ShouldBe((ReportStatus.Done, (Guid?)media, 120L, (DateTimeOffset?)Now.AddSeconds(3)));

        var failed = GeneratedReport.Request(Tenant, "alerts", "Alerts", "{}", ReportFormat.Pdf, User, Now);
        failed.Fail(new string('x', 900), Now);
        failed.Status.ShouldBe(ReportStatus.Failed);
        failed.Error!.Length.ShouldBe(500);
        Should.Throw<DomainException>(() => GeneratedReport.Request(Tenant, "nope", "T", "{}", ReportFormat.Csv, User, Now));
    }

    [Fact]
    public void Archive_items_validate_their_text()
    {
        Should.Throw<DomainException>(() => ArchiveContact.Create(Tenant, " ", null, null, null, false));
        Should.Throw<DomainException>(() => ArchiveNote.Create(Tenant, "", false, User, "A", Now));
        Should.Throw<DomainException>(() => RemoteAccessEntry.Create(Tenant, null, null, "", "Label", "protected", null, Now));

        var note = ArchiveNote.Create(Tenant, " Call back ", true, User, "Platform Support", Now);
        (note.Body, note.IsInternal, note.AuthorName).ShouldBe(("Call back", true, "Platform Support"));
        var profile = CustomerProfile.Create(Tenant, Now);
        profile.Id.ShouldBe(Tenant, "one profile per customer");
        profile.Update(" Retail ", null, "", null, "Sam", Now.AddDays(1));
        (profile.Industry, profile.Phone, profile.AccountManager).ShouldBe(("Retail", null, "Sam"));
    }

    [Fact]
    public void Platform_settings_keep_values_inside_their_ranges()
    {
        var settings = PlatformSettings.Default();
        settings.Id.ShouldBe(PlatformSettings.SingletonId);

        settings.Update("Monitor", 5, 30, 400, "Monitor Cloud", "no-reply@monitor.local", Now);
        settings.OfflineAlertDelayMinutes.ShouldBe(5);
        Should.Throw<DomainException>(() => settings.Update("Monitor", 0, 30, 400, "M", "a@b.test", Now));
        Should.Throw<DomainException>(() => settings.Update("Monitor", 5, 3, 400, "M", "a@b.test", Now));
        Should.Throw<DomainException>(() => settings.Update("", 5, 30, 400, "M", "a@b.test", Now));
    }
}
