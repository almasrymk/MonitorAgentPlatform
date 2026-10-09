using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Abstractions.Storage;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Application.Media.Contracts;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Archive;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Archive;

public static class ArchiveErrors
{
    public static readonly Error NotFound = Error.NotFound("ARCHIVE_ITEM_NOT_FOUND", "Archive item not found.");
}

public sealed record CustomerProfileDto(string CompanyName, string? Industry, string? Website, string? Phone, string? Address, string? AccountManager, DateOnly CustomerSince, string Status);

public sealed record ArchiveContactDto(Guid Id, string Name, string? Email, string? Phone, string? JobTitle, bool IsPrimary);

public sealed record ArchiveNoteDto(Guid Id, string Body, bool IsInternal, string AuthorName, DateTimeOffset CreatedAt);

public sealed record ArchiveFileDto(Guid Id, string DisplayName, long SizeBytes, DateTimeOffset UploadedAt, bool IsInternal);

public sealed record RemoteAccessDto(Guid Id, Guid? LocationId, Guid? DeviceId, string Tool, string Label, string Identifier, bool HasPassword, DateTimeOffset CreatedAt);

public sealed record RemoteAccessSecretDto(Guid Id, string Identifier, string? Password);

/// <summary>Internal items are for platform roles only (02 section 10): the caller must hold <c>archive.internal</c>.</summary>
internal static class ArchiveRules
{
    public static bool SeesInternal(ICurrentUser user) => user.HasPermission(Permissions.ArchiveInternal);

    public static string Mask(string value) => value.Length <= 4 ? "••••" : $"{value[..2]}••••{value[^2..]}";
}

// ---------------------------------------------------------------- profile

[RequirePermission(Permissions.ArchiveRead)]
[RequiresFeature(Features.Archive)]
public sealed record GetCustomerProfileQuery : IQuery<CustomerProfileDto>;

internal sealed class GetCustomerProfileQueryHandler(IReadDbContext db, ITenantContext scope, ITenantDirectory tenants, ITenantFacts facts) : IQueryHandler<GetCustomerProfileQuery, CustomerProfileDto>
{
    public async Task<Result<CustomerProfileDto>> Handle(GetCustomerProfileQuery request, CancellationToken cancellationToken)
    {
        var tenantId = scope.TenantId!.Value;
        var tenant = await tenants.FindAsync(tenantId, cancellationToken);
        var profile = await db.Query<CustomerProfile>().SingleOrDefaultAsync(p => p.TenantId == tenantId, cancellationToken);
        var since = await facts.CustomerSinceAsync(tenantId, cancellationToken);
        return new CustomerProfileDto(tenant?.Name ?? string.Empty, profile?.Industry, profile?.Website, profile?.Phone, profile?.Address, profile?.AccountManager, since, tenant?.Status ?? string.Empty);
    }
}

[RequirePermission(Permissions.ArchiveManage)]
[RequiresFeature(Features.Archive)]
public sealed record UpdateCustomerProfileCommand(string? Industry, string? Website, string? Phone, string? Address, string? AccountManager) : ICommand;

internal sealed class UpdateCustomerProfileCommandValidator : AbstractValidator<UpdateCustomerProfileCommand>
{
    public UpdateCustomerProfileCommandValidator()
    {
        RuleFor(x => x.Industry).MaximumLength(100);
        RuleFor(x => x.Website).MaximumLength(200)
            .Must(w => Uri.TryCreate(w, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            .When(x => !string.IsNullOrWhiteSpace(x.Website)).WithMessage("website must be an http or https address.");
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.Address).MaximumLength(300);
        RuleFor(x => x.AccountManager).MaximumLength(200);
    }
}

internal sealed class UpdateCustomerProfileCommandHandler(IAppDbContext db, ITenantContext scope, IAuditLogger audit, TimeProvider clock) : ICommandHandler<UpdateCustomerProfileCommand>
{
    public async Task<Result> Handle(UpdateCustomerProfileCommand request, CancellationToken cancellationToken)
    {
        var tenantId = scope.TenantId!.Value;
        var profile = await db.Set<CustomerProfile>().SingleOrDefaultAsync(p => p.TenantId == tenantId, cancellationToken);
        if (profile is null)
        {
            profile = CustomerProfile.Create(tenantId, clock.GetUtcNow());
            db.Set<CustomerProfile>().Add(profile);
        }

        profile.Update(request.Industry, request.Website, request.Phone, request.Address, request.AccountManager, clock.GetUtcNow());
        audit.Add("archive.profile_updated", "CustomerProfile", tenantId.ToString());
        return Result.Success();
    }
}

// ---------------------------------------------------------------- contacts

[RequirePermission(Permissions.ArchiveRead)]
[RequiresFeature(Features.Archive)]
public sealed record GetArchiveContactsQuery : IQuery<IReadOnlyList<ArchiveContactDto>>;

internal sealed class GetArchiveContactsQueryHandler(IReadDbContext db) : IQueryHandler<GetArchiveContactsQuery, IReadOnlyList<ArchiveContactDto>>
{
    public async Task<Result<IReadOnlyList<ArchiveContactDto>>> Handle(GetArchiveContactsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<ArchiveContactDto> items = await db.Query<ArchiveContact>().OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Name)
            .Select(c => new ArchiveContactDto(c.Id, c.Name, c.Email, c.Phone, c.JobTitle, c.IsPrimary)).ToListAsync(cancellationToken);
        return Result.Success(items);
    }
}

public sealed record ContactInput(string Name, string? Email, string? Phone, string? JobTitle, bool IsPrimary);

[RequirePermission(Permissions.ArchiveManage)]
[RequiresFeature(Features.Archive)]
public sealed record SaveArchiveContactCommand(Guid? Id, ContactInput Contact) : ICommand<ArchiveContactDto>;

internal sealed class SaveArchiveContactCommandValidator : AbstractValidator<SaveArchiveContactCommand>
{
    public SaveArchiveContactCommandValidator()
    {
        RuleFor(x => x.Contact.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Contact.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrEmpty(x.Contact.Email));
        RuleFor(x => x.Contact.Phone).MaximumLength(50);
        RuleFor(x => x.Contact.JobTitle).MaximumLength(100);
    }
}

internal sealed class SaveArchiveContactCommandHandler(IAppDbContext db, ITenantContext scope, IAuditLogger audit) : ICommandHandler<SaveArchiveContactCommand, ArchiveContactDto>
{
    public async Task<Result<ArchiveContactDto>> Handle(SaveArchiveContactCommand request, CancellationToken cancellationToken)
    {
        var c = request.Contact;
        ArchiveContact contact;
        if (request.Id is { } id)
        {
            var existing = await db.Set<ArchiveContact>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (existing is null)
                return ArchiveErrors.NotFound;
            contact = existing;
            contact.Update(c.Name, c.Email, c.Phone, c.JobTitle, c.IsPrimary);
        }
        else
        {
            contact = ArchiveContact.Create(scope.TenantId!.Value, c.Name, c.Email, c.Phone, c.JobTitle, c.IsPrimary);
            db.Set<ArchiveContact>().Add(contact);
        }

        if (c.IsPrimary)
        {
            foreach (var other in await db.Set<ArchiveContact>().Where(x => x.IsPrimary && x.Id != contact.Id).ToListAsync(cancellationToken))
                other.Update(other.Name, other.Email, other.Phone, other.JobTitle, false);
        }

        audit.Add(request.Id is null ? "archive.contact_added" : "archive.contact_updated", "ArchiveContact", contact.Id.ToString(), contact.Name);
        return new ArchiveContactDto(contact.Id, contact.Name, contact.Email, contact.Phone, contact.JobTitle, contact.IsPrimary);
    }
}

[RequirePermission(Permissions.ArchiveManage)]
[RequiresFeature(Features.Archive)]
public sealed record DeleteArchiveContactCommand(Guid Id) : ICommand;

internal sealed class DeleteArchiveContactCommandValidator : AbstractValidator<DeleteArchiveContactCommand>
{
    public DeleteArchiveContactCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal sealed class DeleteArchiveContactCommandHandler(IAppDbContext db, IAuditLogger audit) : ICommandHandler<DeleteArchiveContactCommand>
{
    public async Task<Result> Handle(DeleteArchiveContactCommand request, CancellationToken cancellationToken)
    {
        var contact = await db.Set<ArchiveContact>().SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (contact is null)
            return ArchiveErrors.NotFound;
        db.Set<ArchiveContact>().Remove(contact);
        audit.Add("archive.contact_removed", "ArchiveContact", contact.Id.ToString(), contact.Name);
        return Result.Success();
    }
}

// ---------------------------------------------------------------- notes

[RequirePermission(Permissions.ArchiveRead)]
[RequiresFeature(Features.Archive)]
public sealed record GetArchiveNotesQuery : IQuery<IReadOnlyList<ArchiveNoteDto>>;

internal sealed class GetArchiveNotesQueryHandler(IReadDbContext db, ICurrentUser user) : IQueryHandler<GetArchiveNotesQuery, IReadOnlyList<ArchiveNoteDto>>
{
    public async Task<Result<IReadOnlyList<ArchiveNoteDto>>> Handle(GetArchiveNotesQuery request, CancellationToken cancellationToken)
    {
        var query = db.Query<ArchiveNote>();
        if (!ArchiveRules.SeesInternal(user))
            query = query.Where(n => !n.IsInternal);
        IReadOnlyList<ArchiveNoteDto> items = await query.OrderByDescending(n => n.CreatedAt)
            .Select(n => new ArchiveNoteDto(n.Id, n.Body, n.IsInternal, n.AuthorName, n.CreatedAt)).ToListAsync(cancellationToken);
        return Result.Success(items);
    }
}

[RequirePermission(Permissions.ArchiveManage)]
[RequiresFeature(Features.Archive)]
public sealed record AddArchiveNoteCommand(string Body, bool IsInternal) : ICommand<ArchiveNoteDto>;

internal sealed class AddArchiveNoteCommandValidator : AbstractValidator<AddArchiveNoteCommand>
{
    public AddArchiveNoteCommandValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
}

internal sealed class AddArchiveNoteCommandHandler(IAppDbContext db, ITenantContext scope, ICurrentUser user, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<AddArchiveNoteCommand, ArchiveNoteDto>
{
    public Task<Result<ArchiveNoteDto>> Handle(AddArchiveNoteCommand request, CancellationToken cancellationToken) => Task.FromResult(Add(request));

    private Result<ArchiveNoteDto> Add(AddArchiveNoteCommand request)
    {
        // Internal notes need archive.internal (06: "Internal notes need archive.internal").
        if (request.IsInternal && !ArchiveRules.SeesInternal(user))
            return Error.Forbidden("AUTH_FORBIDDEN", "Internal notes are for platform staff.");
        var note = ArchiveNote.Create(scope.TenantId!.Value, request.Body, request.IsInternal, user.UserId ?? Guid.Empty, user.Name ?? string.Empty, clock.GetUtcNow());
        db.Set<ArchiveNote>().Add(note);
        audit.Add("archive.note_added", "ArchiveNote", note.Id.ToString(), request.IsInternal ? "internal" : null);
        return new ArchiveNoteDto(note.Id, note.Body, note.IsInternal, note.AuthorName, note.CreatedAt);
    }
}

[RequirePermission(Permissions.ArchiveManage)]
[RequiresFeature(Features.Archive)]
public sealed record DeleteArchiveNoteCommand(Guid Id) : ICommand;

internal sealed class DeleteArchiveNoteCommandValidator : AbstractValidator<DeleteArchiveNoteCommand>
{
    public DeleteArchiveNoteCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal sealed class DeleteArchiveNoteCommandHandler(IAppDbContext db, ICurrentUser user, IAuditLogger audit) : ICommandHandler<DeleteArchiveNoteCommand>
{
    public async Task<Result> Handle(DeleteArchiveNoteCommand request, CancellationToken cancellationToken)
    {
        var note = await db.Set<ArchiveNote>().SingleOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (note is null || (note.IsInternal && !ArchiveRules.SeesInternal(user)))
            return ArchiveErrors.NotFound;
        db.Set<ArchiveNote>().Remove(note);
        audit.Add("archive.note_removed", "ArchiveNote", note.Id.ToString());
        return Result.Success();
    }
}

// ---------------------------------------------------------------- files

[RequirePermission(Permissions.ArchiveRead)]
[RequiresFeature(Features.Archive)]
public sealed record GetArchiveFilesQuery : IQuery<IReadOnlyList<ArchiveFileDto>>;

internal sealed class GetArchiveFilesQueryHandler(IReadDbContext db, ICurrentUser user) : IQueryHandler<GetArchiveFilesQuery, IReadOnlyList<ArchiveFileDto>>
{
    public async Task<Result<IReadOnlyList<ArchiveFileDto>>> Handle(GetArchiveFilesQuery request, CancellationToken cancellationToken)
    {
        var query = db.Query<ArchiveFile>();
        if (!ArchiveRules.SeesInternal(user))
            query = query.Where(f => !f.IsInternal);
        IReadOnlyList<ArchiveFileDto> items = await query.OrderByDescending(f => f.UploadedAt)
            .Select(f => new ArchiveFileDto(f.Id, f.DisplayName, f.SizeBytes, f.UploadedAt, f.IsInternal)).ToListAsync(cancellationToken);
        return Result.Success(items);
    }
}

/// <summary>Multipart upload (10 MB, allowed types only; 06).</summary>
[RequirePermission(Permissions.ArchiveManage)]
[RequiresFeature(Features.Archive)]
public sealed record UploadArchiveFileCommand(string FileName, byte[] Content, string? DisplayName, bool IsInternal) : ICommand<ArchiveFileDto>;

internal sealed class UploadArchiveFileCommandValidator : AbstractValidator<UploadArchiveFileCommand>
{
    public UploadArchiveFileCommandValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Content).NotNull();
        RuleFor(x => x.DisplayName).MaximumLength(200);
    }
}

internal sealed class UploadArchiveFileCommandHandler(IAppDbContext db, ITenantContext scope, ICurrentUser user, IMediaFiles media, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<UploadArchiveFileCommand, ArchiveFileDto>
{
    public async Task<Result<ArchiveFileDto>> Handle(UploadArchiveFileCommand request, CancellationToken cancellationToken)
    {
        if (request.IsInternal && !ArchiveRules.SeesInternal(user))
            return Error.Forbidden("AUTH_FORBIDDEN", "Internal files are for platform staff.");
        var tenantId = scope.TenantId!.Value;
        var stored = await media.SaveAsync(tenantId, request.FileName, request.Content, cancellationToken);
        var file = ArchiveFile.Create(tenantId, stored.Id, string.IsNullOrWhiteSpace(request.DisplayName) ? stored.FileName : request.DisplayName, stored.SizeBytes, user.UserId ?? Guid.Empty,
            request.IsInternal, clock.GetUtcNow());
        db.Set<ArchiveFile>().Add(file);
        audit.Add("archive.file_uploaded", "ArchiveFile", file.Id.ToString(), file.DisplayName);
        return new ArchiveFileDto(file.Id, file.DisplayName, file.SizeBytes, file.UploadedAt, file.IsInternal);
    }
}

[RequirePermission(Permissions.ArchiveRead)]
[RequiresFeature(Features.Archive)]
public sealed record DownloadArchiveFileQuery(Guid Id) : IQuery<DownloadDto>;

internal sealed class DownloadArchiveFileQueryValidator : AbstractValidator<DownloadArchiveFileQuery>
{
    public DownloadArchiveFileQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal sealed class DownloadArchiveFileQueryHandler(IReadDbContext db, ICurrentUser user, IMediaFiles media) : IQueryHandler<DownloadArchiveFileQuery, DownloadDto>
{
    public async Task<Result<DownloadDto>> Handle(DownloadArchiveFileQuery request, CancellationToken cancellationToken)
    {
        var file = await db.Query<ArchiveFile>().SingleOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (file is null || (file.IsInternal && !ArchiveRules.SeesInternal(user)))
            return ArchiveErrors.NotFound;
        var content = await media.OpenAsync(file.MediaId, cancellationToken);
        return content is null ? ArchiveErrors.NotFound : new DownloadDto(content.FileName, content.ContentType, content.Content);
    }
}

[RequirePermission(Permissions.ArchiveManage)]
[RequiresFeature(Features.Archive)]
public sealed record DeleteArchiveFileCommand(Guid Id) : ICommand;

internal sealed class DeleteArchiveFileCommandValidator : AbstractValidator<DeleteArchiveFileCommand>
{
    public DeleteArchiveFileCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal sealed class DeleteArchiveFileCommandHandler(IAppDbContext db, ICurrentUser user, IMediaFiles media, IAuditLogger audit) : ICommandHandler<DeleteArchiveFileCommand>
{
    public async Task<Result> Handle(DeleteArchiveFileCommand request, CancellationToken cancellationToken)
    {
        var file = await db.Set<ArchiveFile>().SingleOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (file is null || (file.IsInternal && !ArchiveRules.SeesInternal(user)))
            return ArchiveErrors.NotFound;
        db.Set<ArchiveFile>().Remove(file);
        await media.DeleteAsync(file.MediaId, cancellationToken);
        audit.Add("archive.file_removed", "ArchiveFile", file.Id.ToString(), file.DisplayName);
        return Result.Success();
    }
}

// ---------------------------------------------------------------- remote access (platform roles only)

[RequirePermission(Permissions.ArchiveInternal)]
[RequiresFeature(Features.Archive)]
public sealed record GetRemoteAccessQuery : IQuery<IReadOnlyList<RemoteAccessDto>>;

internal sealed class GetRemoteAccessQueryHandler(IReadDbContext db, ISecretProtector protector) : IQueryHandler<GetRemoteAccessQuery, IReadOnlyList<RemoteAccessDto>>
{
    public async Task<Result<IReadOnlyList<RemoteAccessDto>>> Handle(GetRemoteAccessQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.Query<RemoteAccessEntry>().OrderBy(r => r.Label).ToListAsync(cancellationToken);
        IReadOnlyList<RemoteAccessDto> items = rows.Select(r => new RemoteAccessDto(
            r.Id, r.LocationId, r.DeviceId, r.Tool, r.Label, ArchiveRules.Mask(protector.Unprotect(r.IdentifierProtected)), r.PasswordProtected is not null, r.CreatedAt)).ToList();
        return Result.Success(items);
    }
}

public sealed record RemoteAccessInput(Guid? LocationId, Guid? DeviceId, string Tool, string Label, string Identifier, string? Password);

[RequirePermission(Permissions.ArchiveInternal)]
[RequiresFeature(Features.Archive)]
public sealed record SaveRemoteAccessCommand(Guid? Id, RemoteAccessInput Entry) : ICommand<RemoteAccessDto>;

internal sealed class SaveRemoteAccessCommandValidator : AbstractValidator<SaveRemoteAccessCommand>
{
    public SaveRemoteAccessCommandValidator()
    {
        RuleFor(x => x.Entry.Tool).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Entry.Label).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Entry.Identifier).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Entry.Password).MaximumLength(500);
    }
}

internal sealed class SaveRemoteAccessCommandHandler(
    IAppDbContext db, ITenantContext scope, ILocationDirectory locations, Devices.Contracts.IDeviceDirectory devices, ISecretProtector protector, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<SaveRemoteAccessCommand, RemoteAccessDto>
{
    public async Task<Result<RemoteAccessDto>> Handle(SaveRemoteAccessCommand request, CancellationToken cancellationToken)
    {
        var e = request.Entry;
        if (e.LocationId is { } locationId && (await locations.ExistingAsync(scope.TenantId!.Value, [locationId], cancellationToken)).Count == 0)
            return Error.NotFound(Common.ErrorCodes.LocationNotFound, "Location not found.");
        if (e.DeviceId is { } deviceId && await devices.FindAsync(deviceId, cancellationToken) is null)
            return Error.NotFound(Common.ErrorCodes.DeviceNotFound, "Device not found.");
        var identifier = protector.Protect(e.Identifier);
        var password = string.IsNullOrEmpty(e.Password) ? null : protector.Protect(e.Password);
        RemoteAccessEntry entry;
        if (request.Id is { } id)
        {
            var existing = await db.Set<RemoteAccessEntry>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (existing is null)
                return ArchiveErrors.NotFound;
            entry = existing;
            entry.Update(e.LocationId, e.DeviceId, e.Tool, e.Label, identifier, password ?? existing.PasswordProtected);
        }
        else
        {
            entry = RemoteAccessEntry.Create(scope.TenantId!.Value, e.LocationId, e.DeviceId, e.Tool, e.Label, identifier, password, clock.GetUtcNow());
            db.Set<RemoteAccessEntry>().Add(entry);
        }

        audit.Add(request.Id is null ? "archive.remote_access_added" : "archive.remote_access_updated", "RemoteAccessEntry", entry.Id.ToString(), entry.Label);
        return new RemoteAccessDto(entry.Id, entry.LocationId, entry.DeviceId, entry.Tool, entry.Label, ArchiveRules.Mask(e.Identifier), entry.PasswordProtected is not null, entry.CreatedAt);
    }
}

/// <summary>Returns the clear values; every reveal is audited (MC-903).</summary>
[RequirePermission(Permissions.ArchiveInternal)]
[RequiresFeature(Features.Archive)]
public sealed record RevealRemoteAccessQuery(Guid Id) : IQuery<RemoteAccessSecretDto>;

internal sealed class RevealRemoteAccessQueryValidator : AbstractValidator<RevealRemoteAccessQuery>
{
    public RevealRemoteAccessQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal sealed class RevealRemoteAccessQueryHandler(IReadDbContext db, ISecretProtector protector, IAuditLogger audit) : IQueryHandler<RevealRemoteAccessQuery, RemoteAccessSecretDto>
{
    public async Task<Result<RemoteAccessSecretDto>> Handle(RevealRemoteAccessQuery request, CancellationToken cancellationToken)
    {
        var entry = await db.Query<RemoteAccessEntry>().SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (entry is null)
            return ArchiveErrors.NotFound;
        // A query that writes: the reveal must be recorded even though queries do not save (written at once).
        await audit.WriteNowAsync("archive.remote_access_revealed", "RemoteAccessEntry", entry.Id.ToString(), entry.Label, true, cancellationToken);
        return new RemoteAccessSecretDto(entry.Id, protector.Unprotect(entry.IdentifierProtected), entry.PasswordProtected is null ? null : protector.Unprotect(entry.PasswordProtected));
    }
}

[RequirePermission(Permissions.ArchiveInternal)]
[RequiresFeature(Features.Archive)]
public sealed record DeleteRemoteAccessCommand(Guid Id) : ICommand;

internal sealed class DeleteRemoteAccessCommandValidator : AbstractValidator<DeleteRemoteAccessCommand>
{
    public DeleteRemoteAccessCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal sealed class DeleteRemoteAccessCommandHandler(IAppDbContext db, IAuditLogger audit) : ICommandHandler<DeleteRemoteAccessCommand>
{
    public async Task<Result> Handle(DeleteRemoteAccessCommand request, CancellationToken cancellationToken)
    {
        var entry = await db.Set<RemoteAccessEntry>().SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (entry is null)
            return ArchiveErrors.NotFound;
        db.Set<RemoteAccessEntry>().Remove(entry);
        audit.Add("archive.remote_access_removed", "RemoteAccessEntry", entry.Id.ToString(), entry.Label);
        return Result.Success();
    }
}
