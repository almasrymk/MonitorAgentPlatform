using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Entitlements;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Notifications;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Notifications;

public static class NotificationErrors
{
    public static readonly Error RecipientNotFound = Error.NotFound("RECIPIENT_NOT_FOUND", "Recipient not found.");
    public static readonly Error RecipientEmailTaken = Error.Conflict("RECIPIENT_EMAIL_TAKEN", "This e-mail address is already a recipient.");
}

public sealed record AlertSettingsDto(bool EmailEnabled, bool SmsEnabled, bool SmsAvailable, bool InAppEnabled, bool WebhookEnabled, string? WebhookUrl, bool EmailEntitled, bool WebhookEntitled);

public sealed record RecipientDto(Guid Id, string Name, string Email, string Events, Guid? LocationId, string? LocationName, bool IsActive);

[RequirePermission(Permissions.SettingsManage)]
public sealed record GetAlertSettingsQuery : IQuery<AlertSettingsDto>;

internal sealed class GetAlertSettingsQueryHandler(IReadDbContext db, ITenantContext scope, IEntitlementReader entitlements, TimeProvider clock) : IQueryHandler<GetAlertSettingsQuery, AlertSettingsDto>
{
    public async Task<Result<AlertSettingsDto>> Handle(GetAlertSettingsQuery request, CancellationToken cancellationToken)
    {
        var tenantId = scope.TenantId!.Value;
        var s = await db.Query<AlertChannelSettings>().SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken) ?? AlertChannelSettings.Default(tenantId, clock.GetUtcNow());
        var plan = await entitlements.GetAsync(tenantId, cancellationToken);
        return new AlertSettingsDto(s.EmailEnabled, false, false, s.InAppEnabled, s.WebhookEnabled, s.WebhookUrl, plan.Has(Features.NotificationsEmail), plan.Has(Features.NotificationsWebhook));
    }
}

[RequirePermission(Permissions.SettingsManage)]
public sealed record UpdateAlertSettingsCommand(bool EmailEnabled, bool InAppEnabled, bool WebhookEnabled, string? WebhookUrl) : ICommand;

internal sealed class UpdateAlertSettingsCommandValidator : AbstractValidator<UpdateAlertSettingsCommand>
{
    public UpdateAlertSettingsCommandValidator() =>
        RuleFor(x => x.WebhookUrl).NotEmpty().MaximumLength(500).Must(u => u!.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(u, UriKind.Absolute, out _))
            .When(x => x.WebhookEnabled).WithMessage("A webhook needs an absolute https URL.");
}

internal sealed class UpdateAlertSettingsCommandHandler(IAppDbContext db, ITenantContext scope, IEntitlementReader entitlements, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<UpdateAlertSettingsCommand>
{
    public static readonly Error WebhookNotEntitled = Error.Forbidden(ErrorCodes.FeatureNotEntitled, "Webhooks are not included in the plan.").With("feature", Features.NotificationsWebhook);

    public async Task<Result> Handle(UpdateAlertSettingsCommand request, CancellationToken cancellationToken)
    {
        var tenantId = scope.TenantId!.Value;
        if (request.WebhookEnabled && !(await entitlements.GetAsync(tenantId, cancellationToken)).Has(Features.NotificationsWebhook))
            return WebhookNotEntitled;
        var now = clock.GetUtcNow();
        var settings = await db.Set<AlertChannelSettings>().SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        if (settings is null)
        {
            settings = AlertChannelSettings.Default(tenantId, now);
            db.Set<AlertChannelSettings>().Add(settings);
        }

        settings.Update(request.EmailEnabled, request.InAppEnabled, request.WebhookEnabled, request.WebhookUrl, now);
        audit.Add("settings.alerts_updated", "AlertChannelSettings", tenantId.ToString(), $"email={request.EmailEnabled}, inApp={request.InAppEnabled}, webhook={request.WebhookEnabled}");
        return Result.Success();
    }
}

[RequirePermission(Permissions.SettingsManage)]
public sealed record GetRecipientsQuery : IQuery<IReadOnlyList<RecipientDto>>;

internal sealed class GetRecipientsQueryHandler(IReadDbContext db, ILocationLookup locations) : IQueryHandler<GetRecipientsQuery, IReadOnlyList<RecipientDto>>
{
    public async Task<Result<IReadOnlyList<RecipientDto>>> Handle(GetRecipientsQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.Query<AlertRecipient>().OrderBy(r => r.Name).ThenBy(r => r.Email).ToListAsync(cancellationToken);
        var names = await locations.GetAsync([.. rows.Where(r => r.LocationId != null).Select(r => r.LocationId!.Value).Distinct()], cancellationToken);
        IReadOnlyList<RecipientDto> items = rows.Select(r => Recipients.ToDto(r, r.LocationId is { } l ? names.GetValueOrDefault(l)?.Name : null)).ToList();
        return Result.Success(items);
    }
}

internal static class Recipients
{
    public static RecipientDto ToDto(AlertRecipient r, string? locationName) => new(r.Id, r.Name, r.Email, r.Events.ToString(), r.LocationId, locationName, r.IsActive);

    public static async Task<Error?> CheckAsync(IAppDbContext db, ILocationDirectory locations, Guid tenantId, string email, Guid? locationId, Guid? exceptId, CancellationToken ct)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (await db.Set<AlertRecipient>().AnyAsync(r => r.Email == normalized && r.Id != exceptId, ct))
            return NotificationErrors.RecipientEmailTaken;
        if (locationId is { } l && (await locations.ExistingAsync(tenantId, [l], ct)).Count == 0)
            return Error.NotFound(ErrorCodes.LocationNotFound, "Location not found.");
        return null;
    }
}

[RequirePermission(Permissions.SettingsManage)]
public sealed record CreateRecipientCommand(string Name, string Email, string Events, Guid? LocationId) : ICommand<RecipientDto>;

internal sealed class CreateRecipientCommandValidator : AbstractValidator<CreateRecipientCommand>
{
    public CreateRecipientCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(x => x.Events).Must(e => Enum.TryParse<RecipientEvents>(e, true, out _)).WithMessage("events must be All, CriticalOnly or WarningsAndCritical.");
    }
}

internal sealed class CreateRecipientCommandHandler(IAppDbContext db, ITenantContext scope, ILocationDirectory locations, IAuditLogger audit) : ICommandHandler<CreateRecipientCommand, RecipientDto>
{
    public async Task<Result<RecipientDto>> Handle(CreateRecipientCommand request, CancellationToken cancellationToken)
    {
        var tenantId = scope.TenantId!.Value;
        if (await Recipients.CheckAsync(db, locations, tenantId, request.Email, request.LocationId, null, cancellationToken) is { } error)
            return error;
        var recipient = AlertRecipient.Create(tenantId, request.Name, request.Email, Enum.Parse<RecipientEvents>(request.Events, true), request.LocationId);
        db.Set<AlertRecipient>().Add(recipient);
        audit.Add("settings.recipient_added", "AlertRecipient", recipient.Id.ToString(), recipient.Email);
        return Recipients.ToDto(recipient, null);
    }
}

[RequirePermission(Permissions.SettingsManage)]
public sealed record UpdateRecipientCommand(Guid Id, string Name, string Email, string Events, Guid? LocationId, bool IsActive) : ICommand<RecipientDto>;

internal sealed class UpdateRecipientCommandValidator : AbstractValidator<UpdateRecipientCommand>
{
    public UpdateRecipientCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(x => x.Events).Must(e => Enum.TryParse<RecipientEvents>(e, true, out _)).WithMessage("events must be All, CriticalOnly or WarningsAndCritical.");
    }
}

internal sealed class UpdateRecipientCommandHandler(IAppDbContext db, ITenantContext scope, ILocationDirectory locations, IAuditLogger audit) : ICommandHandler<UpdateRecipientCommand, RecipientDto>
{
    public async Task<Result<RecipientDto>> Handle(UpdateRecipientCommand request, CancellationToken cancellationToken)
    {
        var recipient = await db.Set<AlertRecipient>().SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (recipient is null)
            return NotificationErrors.RecipientNotFound;
        if (await Recipients.CheckAsync(db, locations, scope.TenantId!.Value, request.Email, request.LocationId, recipient.Id, cancellationToken) is { } error)
            return error;
        recipient.Update(request.Name, request.Email, Enum.Parse<RecipientEvents>(request.Events, true), request.LocationId, request.IsActive);
        audit.Add("settings.recipient_updated", "AlertRecipient", recipient.Id.ToString(), recipient.Email);
        return Recipients.ToDto(recipient, null);
    }
}

[RequirePermission(Permissions.SettingsManage)]
public sealed record DeleteRecipientCommand(Guid Id) : ICommand;

internal sealed class DeleteRecipientCommandValidator : AbstractValidator<DeleteRecipientCommand>
{
    public DeleteRecipientCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal sealed class DeleteRecipientCommandHandler(IAppDbContext db, IAuditLogger audit) : ICommandHandler<DeleteRecipientCommand>
{
    public async Task<Result> Handle(DeleteRecipientCommand request, CancellationToken cancellationToken)
    {
        var recipient = await db.Set<AlertRecipient>().SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (recipient is null)
            return NotificationErrors.RecipientNotFound;
        db.Set<AlertRecipient>().Remove(recipient);
        audit.Add("settings.recipient_removed", "AlertRecipient", recipient.Id.ToString(), recipient.Email);
        return Result.Success();
    }
}
