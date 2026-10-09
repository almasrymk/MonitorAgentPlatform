using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Entitlements;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Abstractions.Storage;
using MonitorCloud.Application.Common;
using MonitorCloud.Application.Licensing.Contracts;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Notifications;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Notifications;

public sealed record IntegrationsDto(bool WebhookEnabled, string? WebhookUrl, bool HasSecret, bool WebhookEntitled);

public sealed record WebhookTestDto(bool Success, int? StatusCode, string? Error);

/// <summary>Settings > Integrations (MC-904): the webhook URL and signing secret; delivery needs <c>notifications.webhook</c>.</summary>
[RequirePermission(Permissions.SettingsManage)]
public sealed record GetIntegrationsQuery : IQuery<IntegrationsDto>;

internal sealed class GetIntegrationsQueryHandler(IReadDbContext db, ITenantContext scope, IEntitlementReader entitlements, TimeProvider clock) : IQueryHandler<GetIntegrationsQuery, IntegrationsDto>
{
    public async Task<Result<IntegrationsDto>> Handle(GetIntegrationsQuery request, CancellationToken cancellationToken)
    {
        var tenantId = scope.TenantId!.Value;
        var s = await db.Query<AlertChannelSettings>().SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken) ?? AlertChannelSettings.Default(tenantId, clock.GetUtcNow());
        var entitled = (await entitlements.GetAsync(tenantId, cancellationToken)).Has(Features.NotificationsWebhook);
        return new IntegrationsDto(s.WebhookEnabled, s.WebhookUrl, s.WebhookSecretProtected is not null, entitled);
    }
}

/// <summary><paramref name="Secret"/> null keeps the stored secret; an empty string removes it.</summary>
[RequirePermission(Permissions.SettingsManage)]
public sealed record UpdateIntegrationsCommand(bool WebhookEnabled, string? WebhookUrl, string? Secret) : ICommand<IntegrationsDto>;

internal sealed class UpdateIntegrationsCommandValidator : AbstractValidator<UpdateIntegrationsCommand>
{
    public UpdateIntegrationsCommandValidator()
    {
        RuleFor(x => x.WebhookUrl).NotEmpty().MaximumLength(500).Must(u => u!.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(u, UriKind.Absolute, out _))
            .When(x => x.WebhookEnabled).WithMessage("A webhook needs an absolute https URL.");
        RuleFor(x => x.Secret).MaximumLength(200);
    }
}

internal sealed class UpdateIntegrationsCommandHandler(IAppDbContext db, ITenantContext scope, IEntitlementReader entitlements, ISecretProtector protector, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<UpdateIntegrationsCommand, IntegrationsDto>
{
    public async Task<Result<IntegrationsDto>> Handle(UpdateIntegrationsCommand request, CancellationToken cancellationToken)
    {
        var tenantId = scope.TenantId!.Value;
        var entitled = (await entitlements.GetAsync(tenantId, cancellationToken)).Has(Features.NotificationsWebhook);
        if (request.WebhookEnabled && !entitled)
            return UpdateAlertSettingsCommandHandler.WebhookNotEntitled;
        var now = clock.GetUtcNow();
        var settings = await db.Set<AlertChannelSettings>().SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        if (settings is null)
        {
            settings = AlertChannelSettings.Default(tenantId, now);
            db.Set<AlertChannelSettings>().Add(settings);
        }

        settings.Update(settings.EmailEnabled, settings.InAppEnabled, request.WebhookEnabled, request.WebhookEnabled ? request.WebhookUrl : settings.WebhookUrl, now);
        if (request.Secret is not null)
            settings.SetWebhookSecret(request.Secret.Length == 0 ? null : protector.Protect(request.Secret), now);
        audit.Add("settings.integrations_updated", "AlertChannelSettings", tenantId.ToString(), $"webhook={request.WebhookEnabled}");
        return new IntegrationsDto(settings.WebhookEnabled, settings.WebhookUrl, settings.WebhookSecretProtected is not null, entitled);
    }
}

/// <summary>"Test" button: posts a sample payload to the stored webhook and reports the answer.</summary>
[RequirePermission(Permissions.SettingsManage)]
[RequiresFeature(Features.NotificationsWebhook)]
public sealed record TestWebhookCommand : ICommand<WebhookTestDto>;

internal sealed class TestWebhookCommandValidator : AbstractValidator<TestWebhookCommand>;

internal sealed class TestWebhookCommandHandler(IReadDbContext db, ITenantContext scope, ISecretProtector protector, IWebhookSender sender, IAuditLogger audit, TimeProvider clock)
    : ICommandHandler<TestWebhookCommand, WebhookTestDto>
{
    public static readonly Error NoWebhook = Error.Validation(ErrorCodes.ValidationFailed, "Save a webhook URL first.");

    public async Task<Result<WebhookTestDto>> Handle(TestWebhookCommand request, CancellationToken cancellationToken)
    {
        var tenantId = scope.TenantId!.Value;
        var settings = await db.Query<AlertChannelSettings>().SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        if (settings?.WebhookUrl is not { } url)
            return NoWebhook;
        var payload = JsonSerializer.Serialize(new { type = "test", tenantId, at = clock.GetUtcNow(), message = "Test notification from Monitor Cloud." }, WebhookPayloads.Json);
        var result = await sender.SendAsync(url, settings.WebhookSecretProtected is { } s ? protector.Unprotect(s) : null, payload, cancellationToken);
        audit.Add("settings.webhook_tested", "AlertChannelSettings", tenantId.ToString(), result.Success ? $"HTTP {result.StatusCode}" : result.Error, result.Success);
        return new WebhookTestDto(result.Success, result.StatusCode, result.Error);
    }
}

internal static class WebhookPayloads
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
