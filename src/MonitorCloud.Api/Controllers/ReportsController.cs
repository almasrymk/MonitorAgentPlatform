using MediatR;
using Microsoft.AspNetCore.Mvc;
using MonitorCloud.Application.Abstractions.Storage;
using MonitorCloud.Application.Archive;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Notifications;
using MonitorCloud.Application.Reports;
using MonitorCloud.Application.Tenancy;
using MonitorCloud.Domain.Media;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Api.Controllers;

/// <summary>Maps a download result to a file stream (the stream is disposed by the response).</summary>
public abstract class FileControllerBase(ISender sender) : ApiControllerBase(sender)
{
    protected IActionResult FromDownload(Result<DownloadDto> result) =>
        result.IsSuccess ? File(result.Value.Content, result.Value.ContentType, result.Value.FileName) : Problem(result.Error!);
}

[Route("api/v1/reports")]
public sealed class ReportsController(ISender sender) : FileControllerBase(sender)
{
    public sealed record ReportRequest(
        string? Type, IReadOnlyList<Guid>? LocationIds, IReadOnlyList<Guid>? DeviceIds, DateTimeOffset? From, DateTimeOffset? To, string? GroupBy, string? Format);

    [HttpGet("types")]
    [ProducesResponseType<ReportTypesDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Types(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetReportTypesQuery(), cancellationToken));

    [HttpPost]
    [ProducesResponseType<ReportDto>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Generate(ReportRequest r, CancellationToken cancellationToken)
    {
        var result = await Sender.Send(new RequestReportCommand(r.Type ?? string.Empty, r.LocationIds, r.DeviceIds, r.From, r.To, r.GroupBy, r.Format), cancellationToken);
        return result.IsSuccess ? Accepted(result.Value) : Problem(result.Error!);
    }

    [HttpGet]
    [ListEndpoint]
    [ProducesResponseType<PagedResult<ReportDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? sort, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetReportsQuery(sort, page, pageSize), cancellationToken));

    [HttpGet("{id:guid}/download")]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK, "application/octet-stream")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken) =>
        FromDownload(await Sender.Send(new DownloadReportQuery(id), cancellationToken));
}

[Route("api/v1/archive")]
public sealed class ArchiveController(ISender sender) : FileControllerBase(sender)
{
    public sealed record ProfileRequest(string? Industry, string? Website, string? Phone, string? Address, string? AccountManager);

    public sealed record NoteRequest(string? Body, bool? IsInternal);

    [HttpGet("profile")]
    [ProducesResponseType<CustomerProfileDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetCustomerProfileQuery(), cancellationToken));

    [HttpPut("profile")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateProfile(ProfileRequest r, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateCustomerProfileCommand(r.Industry, r.Website, r.Phone, r.Address, r.AccountManager), cancellationToken));

    [HttpGet("contacts")]
    [ProducesResponseType<IReadOnlyList<ArchiveContactDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Contacts(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetArchiveContactsQuery(), cancellationToken));

    [HttpPost("contacts")]
    [ProducesResponseType<ArchiveContactDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddContact(ContactInput contact, CancellationToken cancellationToken) =>
        Created(await Sender.Send(new SaveArchiveContactCommand(null, contact), cancellationToken));

    [HttpPut("contacts/{id:guid}")]
    [ProducesResponseType<ArchiveContactDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> UpdateContact(Guid id, ContactInput contact, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SaveArchiveContactCommand(id, contact), cancellationToken));

    [HttpDelete("contacts/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> DeleteContact(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeleteArchiveContactCommand(id), cancellationToken));

    [HttpGet("notes")]
    [ProducesResponseType<IReadOnlyList<ArchiveNoteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Notes(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetArchiveNotesQuery(), cancellationToken));

    [HttpPost("notes")]
    [ProducesResponseType<ArchiveNoteDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddNote(NoteRequest r, CancellationToken cancellationToken) =>
        Created(await Sender.Send(new AddArchiveNoteCommand(r.Body ?? string.Empty, r.IsInternal ?? false), cancellationToken));

    [HttpDelete("notes/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> DeleteNote(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeleteArchiveNoteCommand(id), cancellationToken));

    [HttpGet("files")]
    [ProducesResponseType<IReadOnlyList<ArchiveFileDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Files(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetArchiveFilesQuery(), cancellationToken));

    /// <summary>Multipart upload (field <c>file</c>); the 10 MB limit is enforced again by the domain.</summary>
    [HttpPost("files")]
    [RequestSizeLimit(MediaFile.MaxBytes + (1 << 20))]
    [RequestFormLimits(MultipartBodyLengthLimit = MediaFile.MaxBytes + (1 << 20))]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ArchiveFileDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] string? displayName, [FromForm] bool? isInternal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        byte[] content;
        if (file.Length > MediaFile.MaxBytes)
        {
            // Not read: the handler rejects by size without the bytes.
            content = new byte[MediaFile.MaxBytes + 1];
        }
        else
        {
            using var buffer = new MemoryStream((int)file.Length);
            await file.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        return Created(await Sender.Send(new UploadArchiveFileCommand(file.FileName, content, displayName, isInternal ?? false), cancellationToken));
    }

    [HttpGet("files/{id:guid}/download")]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK, "application/octet-stream")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> DownloadFile(Guid id, CancellationToken cancellationToken) =>
        FromDownload(await Sender.Send(new DownloadArchiveFileQuery(id), cancellationToken));

    [HttpDelete("files/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> DeleteFile(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeleteArchiveFileCommand(id), cancellationToken));

    [HttpGet("remote-access")]
    [ProducesResponseType<IReadOnlyList<RemoteAccessDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoteAccess(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetRemoteAccessQuery(), cancellationToken));

    [HttpPost("remote-access")]
    [ProducesResponseType<RemoteAccessDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddRemoteAccess(RemoteAccessInput entry, CancellationToken cancellationToken) =>
        Created(await Sender.Send(new SaveRemoteAccessCommand(null, entry), cancellationToken));

    [HttpPut("remote-access/{id:guid}")]
    [ProducesResponseType<RemoteAccessDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> UpdateRemoteAccess(Guid id, RemoteAccessInput entry, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new SaveRemoteAccessCommand(id, entry), cancellationToken));

    [HttpGet("remote-access/{id:guid}/reveal")]
    [ProducesResponseType<RemoteAccessSecretDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Reveal(Guid id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return FromResult(await Sender.Send(new RevealRemoteAccessQuery(id), cancellationToken));
    }

    [HttpDelete("remote-access/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> DeleteRemoteAccess(Guid id, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new DeleteRemoteAccessCommand(id), cancellationToken));
}

[Route("api/v1/settings/integrations")]
public sealed class IntegrationsController(ISender sender) : ApiControllerBase(sender)
{
    public sealed record IntegrationsRequest(bool? WebhookEnabled, string? WebhookUrl, string? Secret);

    [HttpGet]
    [ProducesResponseType<IntegrationsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetIntegrationsQuery(), cancellationToken));

    [HttpPut]
    [ProducesResponseType<IntegrationsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(IntegrationsRequest r, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new UpdateIntegrationsCommand(r.WebhookEnabled ?? false, r.WebhookUrl, r.Secret), cancellationToken));

    [HttpPost("webhook/test")]
    [ProducesResponseType<WebhookTestDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Test(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new TestWebhookCommand(), cancellationToken));
}

[Route("api/v1/platform")]
public sealed class PlatformReportsController(ISender sender) : ApiControllerBase(sender)
{
    [HttpGet("reports/{type}")]
    [ProducesResponseType<ReportData>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Report(string type, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetPlatformReportQuery(type, from, to), cancellationToken));

    [HttpGet("settings")]
    [ProducesResponseType<PlatformSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(new GetPlatformSettingsQuery(), cancellationToken));

    [HttpPut("settings")]
    [ProducesResponseType<PlatformSettingsDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings(UpdatePlatformSettingsCommand command, CancellationToken cancellationToken) =>
        FromResult(await Sender.Send(command, cancellationToken));
}
