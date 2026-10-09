using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Entitlements;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Abstractions.Storage;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Tenancy.Contracts;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Application.Media.Contracts;
using MonitorCloud.Domain.Reports;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Reports;

public static class ReportErrors
{
    public static readonly Error NotFound = Error.NotFound("REPORT_NOT_FOUND", "Report not found.");
    public static readonly Error NotReady = Error.Conflict("REPORT_NOT_READY", "The report is not ready yet.");
    public static readonly Error PdfUnavailable = Error.Conflict("REPORT_PDF_UNAVAILABLE", "PDF is not available on this server; choose CSV.");
}

public sealed record ReportTypeDto(string Type, string Title, string Feature, bool Advanced, bool Entitled);

public sealed record ReportTypesDto(IReadOnlyList<ReportTypeDto> Types, bool PdfAvailable);

public sealed record ReportDto(Guid Id, string Type, string Title, string Format, string Status, long SizeBytes, DateTimeOffset RequestedAt, DateTimeOffset? CompletedAt, string? Error);

[RequirePermission(Permissions.ReportsRead)]
public sealed record GetReportTypesQuery : IQuery<ReportTypesDto>;

internal sealed class GetReportTypesQueryHandler(ITenantContext scope, IEntitlementReader entitlements, IPdfRenderer pdf) : IQueryHandler<GetReportTypesQuery, ReportTypesDto>
{
    public async Task<Result<ReportTypesDto>> Handle(GetReportTypesQuery request, CancellationToken cancellationToken)
    {
        var plan = await entitlements.GetAsync(scope.TenantId!.Value, cancellationToken);
        return new ReportTypesDto([.. ReportCatalog.All.Select(t => new ReportTypeDto(t.Type, t.Title, t.Feature, t.Advanced, plan.Has(t.Feature)))], pdf.IsAvailable);
    }
}

/// <summary>Queues a report (202); the background job generates it (MC-901). The type's plan feature is checked here.</summary>
[RequirePermission(Permissions.ReportsGenerate)]
public sealed record RequestReportCommand(string Type, IReadOnlyList<Guid>? LocationIds, IReadOnlyList<Guid>? DeviceIds, DateTimeOffset? From, DateTimeOffset? To, string? GroupBy, string? Format)
    : ICommand<ReportDto>;

internal sealed class RequestReportCommandValidator : AbstractValidator<RequestReportCommand>
{
    public RequestReportCommandValidator()
    {
        RuleFor(x => x.Type).Must(t => ReportCatalog.Find(t) is not null).WithMessage("Unknown report type.");
        RuleFor(x => x.Format).Must(f => f is null or "csv" or "pdf" or "Csv" or "Pdf").WithMessage("format must be csv or pdf.");
        RuleFor(x => x.GroupBy).Must(g => g is null or "device" or "location").WithMessage("groupBy must be device or location.");
        RuleFor(x => x).Must(x => x.From is null || x.To is null || x.From < x.To).WithMessage("from must be before to.");
        RuleFor(x => x).Must(x => (x.To ?? DateTimeOffset.MaxValue) - (x.From ?? DateTimeOffset.MinValue) <= TimeSpan.FromDays(400) || x.From is null || x.To is null)
            .WithMessage("A report covers at most 400 days.");
        RuleFor(x => x.LocationIds).Must(l => l is null || l.Count <= 200);
        RuleFor(x => x.DeviceIds).Must(d => d is null || d.Count <= 1000);
    }
}

internal sealed class RequestReportCommandHandler(
    IAppDbContext db, ITenantContext scope, ICurrentUser user, IEntitlementReader entitlements, IPdfRenderer pdf, ILocationDirectory locations, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<RequestReportCommand, ReportDto>
{
    public async Task<Result<ReportDto>> Handle(RequestReportCommand request, CancellationToken cancellationToken)
    {
        var tenantId = scope.TenantId!.Value;
        var type = ReportCatalog.Find(request.Type)!;
        if (!(await entitlements.GetAsync(tenantId, cancellationToken)).Has(type.Feature))
            return CommonErrors.FeatureNotEntitled(type.Feature);
        var format = string.Equals(request.Format, "pdf", StringComparison.OrdinalIgnoreCase) ? ReportFormat.Pdf : ReportFormat.Csv;
        if (format == ReportFormat.Pdf && !pdf.IsAvailable)
            return ReportErrors.PdfUnavailable;
        var locationIds = request.LocationIds ?? [];
        if (locationIds.Count > 0 && (await locations.ExistingAsync(tenantId, locationIds, cancellationToken)).Count != locationIds.Distinct().Count())
            return Error.NotFound(ErrorCodes.LocationNotFound, "Location not found.");
        IReadOnlyList<Guid>? restricted = scope.LocationScope.Count > 0 ? [.. scope.LocationScope] : null;
        if (restricted is not null && locationIds.Any(id => !restricted.Contains(id)))
            return Error.NotFound(ErrorCodes.LocationNotFound, "Location not found.");
        var now = clock.GetUtcNow();
        var parameters = new ReportParameters(locationIds, request.DeviceIds ?? [], request.From ?? now.AddDays(-7), request.To ?? now, request.GroupBy ?? "device", restricted);
        var title = $"{type.Title} {parameters.From.UtcDateTime:yyyy-MM-dd} - {parameters.To.UtcDateTime:yyyy-MM-dd}";
        var report = GeneratedReport.Request(tenantId, type.Type, title, JsonSerializer.Serialize(parameters, ReportJson.Options), format, user.UserId ?? Guid.Empty, now);
        db.Set<GeneratedReport>().Add(report);
        audit.Add("report.requested", "GeneratedReport", report.Id.ToString(), $"{type.Type} ({format})");
        return ReportDtos.From(report);
    }
}

[RequirePermission(Permissions.ReportsRead)]
public sealed record GetReportsQuery(string? Sort, int? Page, int? PageSize) : IQuery<PagedResult<ReportDto>>;

internal sealed class GetReportsQueryValidator : AbstractValidator<GetReportsQuery>
{
    public GetReportsQueryValidator() => RuleFor(x => x.Sort).Must(s => s is null or "requestedAt" or "-requestedAt").WithMessage("sort must be requestedAt.");
}

/// <summary>A location-restricted user sees only the reports they requested (others may cover locations outside their scope).</summary>
internal static class ReportVisibility
{
    public static IQueryable<GeneratedReport> Visible(IReadDbContext db, ITenantContext scope, ICurrentUser user)
    {
        var reports = db.Query<GeneratedReport>();
        if (scope.LocationScope.Count == 0)
            return reports;
        var userId = user.UserId ?? Guid.Empty;
        return reports.Where(r => r.RequestedByUserId == userId);
    }
}

internal sealed class GetReportsQueryHandler(IReadDbContext db, ITenantContext scope, ICurrentUser user) : IQueryHandler<GetReportsQuery, PagedResult<ReportDto>>
{
    public async Task<Result<PagedResult<ReportDto>>> Handle(GetReportsQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);
        var visible = ReportVisibility.Visible(db, scope, user);
        var query = request.Sort == "-requestedAt"
            ? visible.OrderBy(r => r.RequestedAt).ThenBy(r => r.Id)
            : visible.OrderByDescending(r => r.RequestedAt).ThenBy(r => r.Id);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<ReportDto>([.. rows.Select(ReportDtos.From)], total, page, pageSize);
    }
}

[RequirePermission(Permissions.ReportsRead)]
public sealed record DownloadReportQuery(Guid Id) : IQuery<DownloadDto>;

internal sealed class DownloadReportQueryValidator : AbstractValidator<DownloadReportQuery>
{
    public DownloadReportQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

internal sealed class DownloadReportQueryHandler(IReadDbContext db, ITenantContext scope, ICurrentUser user, IMediaFiles media) : IQueryHandler<DownloadReportQuery, DownloadDto>
{
    public async Task<Result<DownloadDto>> Handle(DownloadReportQuery request, CancellationToken cancellationToken)
    {
        var report = await ReportVisibility.Visible(db, scope, user).SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken);
        if (report is null)
            return ReportErrors.NotFound;
        if (report.Status != ReportStatus.Done || report.MediaId is not { } mediaId)
            return ReportErrors.NotReady;
        var file = await media.OpenAsync(mediaId, cancellationToken);
        return file is null ? ReportErrors.NotFound : new DownloadDto(file.FileName, file.ContentType, file.Content);
    }
}

/// <summary>Cross-customer reports for platform staff (06: <c>/platform/reports/{type}</c>), returned as data.</summary>
[PlatformOnly]
[RequirePermission(Permissions.PlatformDashboardRead)]
public sealed record GetPlatformReportQuery(string Type, DateTimeOffset? From, DateTimeOffset? To) : IQuery<ReportData>;

internal sealed class GetPlatformReportQueryValidator : AbstractValidator<GetPlatformReportQuery>
{
    public GetPlatformReportQueryValidator() => RuleFor(x => x.Type).Must(t => ReportCatalog.Find(t) is not null).WithMessage("Unknown report type.");
}

internal sealed class GetPlatformReportQueryHandler(ReportEngine engine, TimeProvider clock) : IQueryHandler<GetPlatformReportQuery, ReportData>
{
    public async Task<Result<ReportData>> Handle(GetPlatformReportQuery request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return await engine.BuildAsync(request.Type, new ReportParameters([], [], request.From ?? now.AddDays(-7), request.To ?? now, "device"), null, platform: true, cancellationToken);
    }
}

internal static class ReportDtos
{
    public static ReportDto From(GeneratedReport r) => new(r.Id, r.Type, r.Title, r.Format.ToString(), r.Status.ToString(), r.SizeBytes, r.RequestedAt, r.CompletedAt, r.Error);
}

internal static class ReportJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

/// <summary>Generates queued reports in each report's tenant scope (MC-901); used by the reports job.</summary>
public sealed partial class ReportGenerationService(
    IAppDbContext db, IUnitOfWork unitOfWork, ITenantScopeSetter scope, ReportEngine engine, IPdfRenderer pdf, IMediaFiles media, ITenantDirectory tenants, TimeProvider clock,
    ILogger<ReportGenerationService> logger)
{
    /// <summary>Generates the oldest queued report; returns false when none is waiting. Call in the system scope.</summary>
    public async Task<bool> RunOneAsync(CancellationToken ct)
    {
        var report = await db.Set<GeneratedReport>().Where(r => r.Status == ReportStatus.Queued).OrderBy(r => r.RequestedAt).FirstOrDefaultAsync(ct);
        if (report is null)
            return false;
        report.Start();
        await unitOfWork.SaveChangesAsync(ct);
        try
        {
            var parameters = JsonSerializer.Deserialize<ReportParameters>(report.ParametersJson, ReportJson.Options)!;
            scope.RunAsTenant(report.TenantId, parameters.Scope);
            var data = await engine.BuildAsync(report.Type, parameters, report.TenantId, platform: false, ct);
            var customer = (await tenants.FindAsync(report.TenantId, ct))?.Name ?? string.Empty;
            var (bytes, extension) = report.Format == ReportFormat.Pdf
                ? (await pdf.RenderAsync(ReportRenderer.Html(data, customer), ct), ".pdf")
                : (ReportRenderer.Csv(data), ".csv");
            var stored = await media.SaveAsync(report.TenantId, $"{report.Type}-{report.RequestedAt.UtcDateTime:yyyyMMdd-HHmm}{extension}", bytes, ct);
            report.Complete(stored.Id, bytes.Length, clock.GetUtcNow());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, report.Id, ex);
            report.Fail(ex.Message, clock.GetUtcNow());
        }

        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Report {ReportId} failed")]
    private static partial void LogFailed(ILogger logger, Guid reportId, Exception exception);
}
