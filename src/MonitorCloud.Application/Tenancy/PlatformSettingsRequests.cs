using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Tenancy;

public sealed record PlatformSettingsDto(string BrandName, int OfflineAlertDelayMinutes, int MinuteRetentionDays, int HourRetentionDays, string EmailSenderName, string EmailSenderAddress);

/// <summary>Platform settings (06: <c>/platform/settings</c>).</summary>
[PlatformOnly]
[RequirePermission(Permissions.PlatformSettingsManage)]
public sealed record GetPlatformSettingsQuery : IQuery<PlatformSettingsDto>;

internal sealed class GetPlatformSettingsQueryHandler(IReadDbContext db) : IQueryHandler<GetPlatformSettingsQuery, PlatformSettingsDto>
{
    public async Task<Result<PlatformSettingsDto>> Handle(GetPlatformSettingsQuery request, CancellationToken cancellationToken)
    {
        var s = await db.Query<PlatformSettings>().SingleOrDefaultAsync(cancellationToken) ?? PlatformSettings.Default();
        return PlatformSettingsMapping.From(s);
    }
}

[PlatformOnly]
[RequirePermission(Permissions.PlatformSettingsManage)]
public sealed record UpdatePlatformSettingsCommand(string BrandName, int OfflineAlertDelayMinutes, int MinuteRetentionDays, int HourRetentionDays, string EmailSenderName, string EmailSenderAddress)
    : ICommand<PlatformSettingsDto>;

internal sealed class UpdatePlatformSettingsCommandValidator : AbstractValidator<UpdatePlatformSettingsCommand>
{
    public UpdatePlatformSettingsCommandValidator()
    {
        RuleFor(x => x.BrandName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.OfflineAlertDelayMinutes).InclusiveBetween(1, 60);
        RuleFor(x => x.MinuteRetentionDays).InclusiveBetween(7, 90);
        RuleFor(x => x.HourRetentionDays).InclusiveBetween(90, 1100);
        RuleFor(x => x.EmailSenderName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.EmailSenderAddress).NotEmpty().EmailAddress().MaximumLength(256);
    }
}

internal sealed class UpdatePlatformSettingsCommandHandler(IAppDbContext db, IAuditLogger audit, TimeProvider clock) : ICommandHandler<UpdatePlatformSettingsCommand, PlatformSettingsDto>
{
    public async Task<Result<PlatformSettingsDto>> Handle(UpdatePlatformSettingsCommand request, CancellationToken cancellationToken)
    {
        var settings = await db.Set<PlatformSettings>().SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = PlatformSettings.Default();
            db.Set<PlatformSettings>().Add(settings);
        }

        settings.Update(request.BrandName, request.OfflineAlertDelayMinutes, request.MinuteRetentionDays, request.HourRetentionDays, request.EmailSenderName, request.EmailSenderAddress,
            clock.GetUtcNow());
        audit.Add("platform.settings_updated", "PlatformSettings", PlatformSettings.SingletonId.ToString());
        return PlatformSettingsMapping.From(settings);
    }
}

internal static class PlatformSettingsMapping
{
    public static PlatformSettingsDto From(PlatformSettings s) =>
        new(s.BrandName, s.OfflineAlertDelayMinutes, s.MinuteRetentionDays, s.HourRetentionDays, s.EmailSenderName, s.EmailSenderAddress);
}

internal sealed class TenantFacts(IReadDbContext db) : ITenantFacts
{
    public async Task<DateOnly> CustomerSinceAsync(Guid tenantId, CancellationToken ct) =>
        await db.Query<Tenant>().Where(t => t.Id == tenantId).Select(t => t.CustomerSince).SingleOrDefaultAsync(ct);
}

internal sealed class PlatformDefaults(IReadDbContext db) : Contracts.IPlatformDefaults
{
    public const int DefaultOfflineDelayMinutes = 2;

    public async Task<int> OfflineAlertDelayMinutesAsync(CancellationToken ct) =>
        await db.Query<PlatformSettings>().Select(s => (int?)s.OfflineAlertDelayMinutes).SingleOrDefaultAsync(ct) ?? DefaultOfflineDelayMinutes;
}