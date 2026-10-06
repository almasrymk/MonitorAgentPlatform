namespace MonitorCloud.Application.Abstractions.Email;

public sealed record EmailMessage(string To, string Subject, string TextBody);

/// <summary>Sends e-mail. Development writes messages to the log and to <c>App_Data/mail</c>.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Links into the portal used in e-mails.</summary>
public interface IPortalLinks
{
    string AcceptInvitation(string token);
}
