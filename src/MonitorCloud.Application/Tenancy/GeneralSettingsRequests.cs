using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Monitoring.Contracts;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Tenancy;

/// <summary>Settings > General (06): time zone, default language and the <c>device-offline</c> alert.</summary>
public sealed record GeneralSettingsDto(string TimeZone, string DefaultLanguage, string OfflineAlertSeverity, int OfflineAlertDelayMinutes);

[RequirePermission(Permissions.SettingsManage)]
public sealed record GetGeneralSettingsQuery : IQuery<GeneralSettingsDto>;

internal sealed class GetGeneralSettingsQueryHandler(IReadDbContext db, ITenantContext scope, IMonitoringSettingsStore monitoring) : IQueryHandler<GetGeneralSettingsQuery, GeneralSettingsDto>
{
    public async Task<Result<GeneralSettingsDto>> Handle(GetGeneralSettingsQuery request, CancellationToken cancellationToken)
    {
        var tenantId = scope.TenantId!.Value;
        var tenant = await db.Query<Tenant>().Where(t => t.Id == tenantId).Select(t => new { t.TimeZone, t.DefaultLanguage }).SingleOrDefaultAsync(cancellationToken);
        if (tenant is null)
            return TenancyErrors.TenantNotFound;
        var offline = await monitoring.GetAsync(tenantId, cancellationToken);
        return new GeneralSettingsDto(tenant.TimeZone, tenant.DefaultLanguage, offline.Severity, offline.DelayMinutes);
    }
}

[RequirePermission(Permissions.SettingsManage)]
public sealed record UpdateGeneralSettingsCommand(string TimeZone, string DefaultLanguage, string OfflineAlertSeverity, int OfflineAlertDelayMinutes) : ICommand<GeneralSettingsDto>;

internal sealed class UpdateGeneralSettingsCommandValidator : AbstractValidator<UpdateGeneralSettingsCommand>
{
    public UpdateGeneralSettingsCommandValidator()
    {
        RuleFor(x => x.TimeZone).Must(TimeZones.IsValid).WithMessage("Unknown time zone.");
        RuleFor(x => x.DefaultLanguage).Must(l => l is "en" or "ar").WithMessage("Language must be 'en' or 'ar'.");
        RuleFor(x => x.OfflineAlertSeverity).Must(s => s is "Critical" or "Warning" or "Info").WithMessage("Severity must be Critical, Warning or Info.");
        RuleFor(x => x.OfflineAlertDelayMinutes).InclusiveBetween(1, 60);
    }
}

internal sealed class UpdateGeneralSettingsCommandHandler(IAppDbContext db, ITenantContext scope, IMonitoringSettingsStore monitoring, IAuditLogger audit)
    : ICommandHandler<UpdateGeneralSettingsCommand, GeneralSettingsDto>
{
    public async Task<Result<GeneralSettingsDto>> Handle(UpdateGeneralSettingsCommand request, CancellationToken cancellationToken)
    {
        var tenantId = scope.TenantId!.Value;
        var tenant = await db.Set<Tenant>().SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
            return TenancyErrors.TenantNotFound;
        tenant.SetPreferences(request.TimeZone, request.DefaultLanguage);
        await monitoring.SetAsync(tenantId, request.OfflineAlertSeverity, request.OfflineAlertDelayMinutes, cancellationToken);
        audit.Add("settings.general_updated", "Tenant", tenantId.ToString(),
            $"timeZone={request.TimeZone}, language={request.DefaultLanguage}, offline={request.OfflineAlertSeverity}/{request.OfflineAlertDelayMinutes}m");
        return new GeneralSettingsDto(tenant.TimeZone, tenant.DefaultLanguage, request.OfflineAlertSeverity, request.OfflineAlertDelayMinutes);
    }
}
