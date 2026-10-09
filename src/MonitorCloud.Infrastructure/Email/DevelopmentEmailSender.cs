using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MonitorCloud.Application.Abstractions.Email;

namespace MonitorCloud.Infrastructure.Email;

/// <summary>Writes e-mails to the log and to <c>App_Data/mail</c> (03 / MC-101). Real delivery arrives with M6.</summary>
internal sealed partial class DevelopmentEmailSender(IHostEnvironment environment, Persistence.DatabaseOptionsAccessor database, TimeProvider clock, ILogger<DevelopmentEmailSender> logger)
    : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        LogMail(logger, message.To, message.Subject);
        var folder = Path.Combine(environment.ContentRootPath, "App_Data", "mail");
        Directory.CreateDirectory(folder);
        var safeTo = string.Concat(message.To.Select(c => char.IsLetterOrDigit(c) || c is '.' or '@' or '-' ? c : '_'));
        var file = Path.Combine(folder, $"{clock.GetUtcNow():yyyyMMdd-HHmmss}-{Guid.CreateVersion7():N}-{safeTo}.eml");
        var content = $"From: {await SenderAsync(cancellationToken)}\nTo: {message.To}\nSubject: {message.Subject}\nDate: {clock.GetUtcNow():R}\n\n{message.TextBody}\n";
        await File.WriteAllTextAsync(file, content, cancellationToken);
    }

    /// <summary>The sender of Platform Settings; the default before they are saved.</summary>
    private async Task<string> SenderAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(database.ConnectionString);
            var sender = await connection.QuerySingleOrDefaultAsync<(string Name, string Address)?>(
                new CommandDefinition("SELECT EmailSenderName AS Name, EmailSenderAddress AS Address FROM tenancy.PlatformSettings", cancellationToken: cancellationToken));
            if (sender is { } s)
                return $"{s.Name} <{s.Address}>";
        }
        catch (SqlException)
        {
            // The development sender never fails a send because of the sender line.
        }

        return "Monitor Cloud <no-reply@monitor.local>";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Development e-mail to {To}: {Subject} (written to App_Data/mail)")]
    private static partial void LogMail(ILogger logger, string to, string subject);
}

public sealed class PortalOptions
{
    public const string Section = "Portal";

    public string BaseUrl { get; set; } = "http://localhost:4300";
}

internal sealed class PortalLinks(Microsoft.Extensions.Options.IOptions<PortalOptions> options) : IPortalLinks
{
    public string AcceptInvitation(string token) =>
        $"{options.Value.BaseUrl.TrimEnd('/')}/accept-invitation?token={Uri.EscapeDataString(token)}";
}
