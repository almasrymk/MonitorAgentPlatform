using System.Collections.Concurrent;
using MonitorCloud.Application.Abstractions.Email;

namespace MonitorCloud.TestShared;

/// <summary>Keeps sent e-mails in memory so tests can read invitation links.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>The token of the last invitation link sent to <paramref name="email"/>.</summary>
    public string LastInvitationToken(string email)
    {
        var message = Sent.Last(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase));
        var marker = "token=";
        var start = message.TextBody.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = message.TextBody.IndexOfAny(['\n', '\r', ' '], start);
        return Uri.UnescapeDataString(message.TextBody[start..(end < 0 ? message.TextBody.Length : end)]);
    }
}
