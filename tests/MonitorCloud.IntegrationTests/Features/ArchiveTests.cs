using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MonitorCloud.Application.Archive;
using MonitorCloud.Application.Licensing;
using MonitorCloud.Application.Notifications;
using MonitorCloud.Domain.Archive;
using MonitorCloud.Domain.Audit;
using MonitorCloud.Domain.Media;
using MonitorCloud.Infrastructure.Licensing.Fake;
using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.IntegrationTests.Features;

/// <summary>Customer archive (MC-902, MC-903): internal visibility, uploads, protected remote access and the integrations tab (MC-904).</summary>
[Collection(SqlCollection.Name)]
public sealed class ArchiveTests(SqlServerFixture sql) : FeatureTestBase(sql)
{
    private static MultipartFormDataContent Upload(byte[] content, string fileName, bool? isInternal = null, string? displayName = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        if (isInternal is not null)
            form.Add(new StringContent(isInternal.Value ? "true" : "false"), "isInternal");
        if (displayName is not null)
            form.Add(new StringContent(displayName), "displayName");
        return form;
    }

    private static Uri U(string url) => new(url, UriKind.Relative);

    [Theory]
    [InlineData(MonitorCloud.Domain.Identity.Roles.Administrator)]
    [InlineData(MonitorCloud.Domain.Identity.Roles.ITManager)]
    public async Task Tenant_roles_never_receive_internal_items(string role)
    {
        using var client = App.ClientFor(World.UserOf(role, World.A));

        var notes = await (await client.GetAsync(U("/api/v1/archive/notes"))).ShouldBeOkAsync<IReadOnlyList<ArchiveNoteDto>>();
        notes.Select(n => n.Id).ShouldBe([World.A.Archive.Note1]);
        var files = await (await client.GetAsync(U("/api/v1/archive/files"))).ShouldBeOkAsync<IReadOnlyList<ArchiveFileDto>>();
        files.Select(f => f.Id).ShouldBe([World.A.Archive.File1]);
        await (await client.GetAsync(U($"/api/v1/archive/files/{World.A.Archive.InternalFile1}/download"))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "ARCHIVE_ITEM_NOT_FOUND");
        (await client.GetAsync(U("/api/v1/archive/remote-access"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync(U($"/api/v1/archive/remote-access/{World.A.Archive.RemoteAccess1}/reveal"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Tenant_users_cannot_create_or_delete_internal_items()
    {
        using var client = App.ClientFor(World.A.Administrator);

        (await client.PostJsonAsync("/api/v1/archive/notes", new { body = "Sneaky", isInternal = true })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var upload = Upload("x"u8.ToArray(), "x.txt", isInternal: true);
        (await client.PostAsync(U("/api/v1/archive/files"), upload)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await (await client.DeleteAsync(U($"/api/v1/archive/notes/{World.A.Archive.InternalNote1}"))).ShouldBeProblemAsync(HttpStatusCode.NotFound, "ARCHIVE_ITEM_NOT_FOUND");
        (await App.InDbAsync(db => db.Set<ArchiveNote>().IgnoreQueryFilters().AnyAsync(n => n.Id == World.A.Archive.InternalNote1))).ShouldBeTrue();
    }

    [Fact]
    public async Task Platform_staff_see_internal_items_in_the_workspace()
    {
        using var client = App.ClientFor(World.PlatformSupport, World.A.Id);

        var notes = await (await client.GetAsync(U("/api/v1/archive/notes"))).ShouldBeOkAsync<IReadOnlyList<ArchiveNoteDto>>();
        notes.Select(n => n.Id).ShouldBe([World.A.Archive.Note1, World.A.Archive.InternalNote1], ignoreOrder: true);
        var added = await (await client.PostJsonAsync("/api/v1/archive/notes", new { body = "Renewal call booked", isInternal = true })).ShouldBeOkAsync<ArchiveNoteDto>(HttpStatusCode.Created);
        added.IsInternal.ShouldBeTrue();
        added.AuthorName.ShouldBe(World.PlatformSupport.FullName);
        (await client.GetAsync(U($"/api/v1/archive/files/{World.A.Archive.InternalFile1}/download"))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Remote_access_is_masked_in_lists_and_every_reveal_is_audited()
    {
        using var client = App.ClientFor(World.PlatformAdmin, World.A.Id);

        var list = await (await client.GetAsync(U("/api/v1/archive/remote-access"))).ShouldBeOkAsync<IReadOnlyList<RemoteAccessDto>>();
        var entry = list.Single();
        entry.Identifier.ShouldBe("12••••89");
        entry.HasPassword.ShouldBeTrue();
        var listJson = await (await client.GetAsync(U("/api/v1/archive/remote-access"))).Content.ReadAsStringAsync();
        listJson.ShouldNotContain(TestWorld.RemoteSecret);

        using var reveal = await client.GetAsync(U($"/api/v1/archive/remote-access/{entry.Id}/reveal"));
        reveal.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var secret = await reveal.ShouldBeOkAsync<RemoteAccessSecretDto>();
        (secret.Identifier, secret.Password).ShouldBe(("123 456 789", TestWorld.RemoteSecret));
        var audit = await App.InDbAsync(db => db.Set<AuditRecord>().IgnoreQueryFilters().SingleAsync(a => a.Action == "archive.remote_access_revealed"));
        audit.EntityId.ShouldBe(entry.Id.ToString());
        audit.TenantId.ShouldBe(World.A.Id);
        audit.Details.ShouldNotBeNull().ShouldNotContain(TestWorld.RemoteSecret);

        // Stored values are encrypted, never plain.
        var stored = await App.InDbAsync(db => db.Set<RemoteAccessEntry>().IgnoreQueryFilters().SingleAsync(r => r.Id == entry.Id));
        stored.PasswordProtected.ShouldNotBeNull().ShouldNotContain(TestWorld.RemoteSecret);
        stored.IdentifierProtected.ShouldNotContain("123 456 789");
    }

    [Fact]
    public async Task Remote_access_entries_can_be_added_updated_and_removed()
    {
        using var client = App.ClientFor(World.PlatformAdmin, World.A.Id);
        var input = new { locationId = World.A.Location2.Id, deviceId = (Guid?)null, tool = "RDP", label = "Server room", identifier = "10.0.0.25", password = (string?)"TEST-ONLY-rdp" };

        var added = await (await client.PostJsonAsync("/api/v1/archive/remote-access", input)).ShouldBeOkAsync<RemoteAccessDto>(HttpStatusCode.Created);
        added.Identifier.ShouldBe("10••••25");
        var updated = await (await client.PutJsonAsync($"/api/v1/archive/remote-access/{added.Id}", input with { label = "Server room B", password = (string?)null }))
            .ShouldBeOkAsync<RemoteAccessDto>();
        updated.Label.ShouldBe("Server room B");
        var revealed = await (await client.GetAsync(U($"/api/v1/archive/remote-access/{added.Id}/reveal"))).ShouldBeOkAsync<RemoteAccessSecretDto>();
        revealed.Password.ShouldBe("TEST-ONLY-rdp", "a null password keeps the stored one");
        await (await client.PostJsonAsync("/api/v1/archive/remote-access", input with { locationId = World.B.Location1.Id }))
            .ShouldBeProblemAsync(HttpStatusCode.NotFound, "LOCATION_NOT_FOUND");
        (await client.DeleteAsync(U($"/api/v1/archive/remote-access/{added.Id}"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync(U($"/api/v1/archive/remote-access/{added.Id}/reveal"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Uploads_are_stored_under_the_tenant_and_download_unchanged()
    {
        using var client = App.ClientFor(World.A.Administrator);
        var bytes = "%PDF-1.4 signed contract"u8.ToArray();

        using var upload = Upload(bytes, @"..\..\..\windows\contract.pdf", displayName: "Signed contract");
        var file = await (await client.PostAsync(U("/api/v1/archive/files"), upload)).ShouldBeOkAsync<ArchiveFileDto>(HttpStatusCode.Created);

        file.DisplayName.ShouldBe("Signed contract");
        file.SizeBytes.ShouldBe(bytes.Length);
        var media = await App.InDbAsync(db => db.Set<MediaFile>().IgnoreQueryFilters().OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).FirstAsync(m => m.SizeBytes == bytes.Length));
        media.FileName.ShouldBe("contract.pdf", "path segments of the client name are dropped");
        media.StoragePath.ShouldStartWith(World.A.Id.ToString("N") + "/");
        var onDisk = Path.GetFullPath(Path.Combine(App.StorageRoot, "media", media.StoragePath));
        onDisk.ShouldStartWith(Path.GetFullPath(Path.Combine(App.StorageRoot, "media")));
        File.Exists(onDisk).ShouldBeTrue();

        using var download = await client.GetAsync(U($"/api/v1/archive/files/{file.Id}/download"));
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(bytes);
        download.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("contract.pdf");
    }

    [Theory]
    [InlineData("tool.exe", 10, HttpStatusCode.UnsupportedMediaType, "UPLOAD_TYPE_NOT_ALLOWED")]
    [InlineData("page.html", 10, HttpStatusCode.UnsupportedMediaType, "UPLOAD_TYPE_NOT_ALLOWED")]
    [InlineData("noextension", 10, HttpStatusCode.UnsupportedMediaType, "UPLOAD_TYPE_NOT_ALLOWED")]
    [InlineData("big.pdf", (10 * 1024 * 1024) + 1, HttpStatusCode.RequestEntityTooLarge, "UPLOAD_TOO_LARGE")]
    [InlineData("empty.pdf", 0, HttpStatusCode.BadRequest, "UPLOAD_EMPTY")]
    [InlineData("../..", 10, HttpStatusCode.BadRequest, "UPLOAD_NAME_INVALID")]
    public async Task Invalid_uploads_are_rejected_and_nothing_is_stored(string fileName, int size, HttpStatusCode status, string code)
    {
        using var client = App.ClientFor(World.A.Administrator);
        var before = await App.InDbAsync(db => db.Set<MediaFile>().IgnoreQueryFilters().CountAsync());

        using var upload = Upload(new byte[size], fileName);
        await (await client.PostAsync(U("/api/v1/archive/files"), upload)).ShouldBeProblemAsync(status, code);

        (await App.InDbAsync(db => db.Set<MediaFile>().IgnoreQueryFilters().CountAsync())).ShouldBe(before);
    }

    [Fact]
    public async Task The_archive_needs_the_plan_feature()
    {
        using var starter = App.ClientFor(World.B.Administrator);

        await (await starter.GetAsync(U("/api/v1/archive/profile"))).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FEATURE_NOT_ENTITLED");
    }

    [Fact]
    public async Task Profile_and_contacts_can_be_edited()
    {
        using var client = App.ClientFor(World.A.Administrator);

        (await client.PutJsonAsync("/api/v1/archive/profile", new { industry = "Logistics", website = "https://alpha.test", phone = "+20 2 0000 0000", address = "1 Sample Street", accountManager = "Sam Sample" }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var profile = await (await client.GetAsync(U("/api/v1/archive/profile"))).ShouldBeOkAsync<CustomerProfileDto>();
        (profile.CompanyName, profile.Industry, profile.AccountManager).ShouldBe((World.A.Tenant.Name, "Logistics", "Sam Sample"));
        await (await client.PutJsonAsync("/api/v1/archive/profile", new { website = "javascript:alert(1)" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        var contact = await (await client.PostJsonAsync("/api/v1/archive/contacts", new { name = "Nour Sample", email = "nour@alpha.test", isPrimary = true }))
            .ShouldBeOkAsync<ArchiveContactDto>(HttpStatusCode.Created);
        var contacts = await (await client.GetAsync(U("/api/v1/archive/contacts"))).ShouldBeOkAsync<IReadOnlyList<ArchiveContactDto>>();
        contacts.Single(c => c.IsPrimary).Id.ShouldBe(contact.Id, "one primary contact per customer");
        await (await client.PostJsonAsync("/api/v1/archive/contacts", new { name = "", email = "not-an-email" })).ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        (await client.DeleteAsync(U($"/api/v1/archive/contacts/{contact.Id}"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Another_tenant_with_the_archive_feature_cannot_reach_these_items()
    {
        App.Services.GetRequiredService<FakeLicensingStore>().ChangePlan(World.LicensingCustomerOf(World.B), "ENTERPRISE", App.Clock.GetUtcNow());
        await App.InSystemScopeAsync(sp => sp.GetRequiredService<LicensingSyncService>().ReconcileAllAsync(CancellationToken.None));
        using var b = App.ClientFor(World.B.Administrator);
        using var platformInB = App.ClientFor(World.PlatformAdmin, World.B.Id);
        var archive = World.A.Archive;

        foreach (var client in new[] { b, platformInB })
        {
            (await client.GetAsync(U("/api/v1/archive/profile"))).StatusCode.ShouldBe(HttpStatusCode.OK, "B now has the feature");
            (await client.GetAsync(U($"/api/v1/archive/files/{archive.File1}/download"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.DeleteAsync(U($"/api/v1/archive/notes/{archive.Note1}"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.PutJsonAsync($"/api/v1/archive/contacts/{archive.Contact1}", new { name = "Hijacked" })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.DeleteAsync(U($"/api/v1/archive/files/{archive.File1}"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await client.GetAsync(U($"/api/v1/reports/{archive.Report1}/download"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            var notes = await (await client.GetAsync(U("/api/v1/archive/notes"))).Content.ReadAsStringAsync();
            notes.ShouldNotContain("ALPHA");
        }

        (await platformInB.GetAsync(U($"/api/v1/archive/remote-access/{archive.RemoteAccess1}/reveal"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await App.InDbAsync(db => db.Set<AuditRecord>().IgnoreQueryFilters().AnyAsync(a => a.Action == "archive.remote_access_revealed"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Integrations_store_the_webhook_secret_encrypted_and_never_return_it()
    {
        using var client = App.ClientFor(World.A.Administrator);

        var saved = await (await client.PutJsonAsync("/api/v1/settings/integrations", new { webhookEnabled = true, webhookUrl = "https://hooks.alpha.test/monitor", secret = "TEST-ONLY-hook-secret" }))
            .ShouldBeOkAsync<IntegrationsDto>();

        saved.HasSecret.ShouldBeTrue();
        var json = await (await client.GetAsync(U("/api/v1/settings/integrations"))).Content.ReadAsStringAsync();
        json.ShouldNotContain("TEST-ONLY-hook-secret");
        var stored = await App.InDbAsync(db => db.Set<MonitorCloud.Domain.Notifications.AlertChannelSettings>().IgnoreQueryFilters().SingleAsync(s => s.TenantId == World.A.Id));
        stored.WebhookSecretProtected.ShouldNotBeNull().ShouldNotContain("TEST-ONLY-hook-secret");
        await (await client.PutJsonAsync("/api/v1/settings/integrations", new { webhookEnabled = true, webhookUrl = "http://hooks.alpha.test" }))
            .ShouldBeProblemAsync(HttpStatusCode.BadRequest, "VALIDATION_FAILED");

        using var starter = App.ClientFor(World.B.Administrator);
        await (await starter.PutJsonAsync("/api/v1/settings/integrations", new { webhookEnabled = true, webhookUrl = "https://hooks.beta.test" }))
            .ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FEATURE_NOT_ENTITLED");
        await (await starter.PostAsync(U("/api/v1/settings/integrations/webhook/test"), null)).ShouldBeProblemAsync(HttpStatusCode.Forbidden, "FEATURE_NOT_ENTITLED");
    }
}
