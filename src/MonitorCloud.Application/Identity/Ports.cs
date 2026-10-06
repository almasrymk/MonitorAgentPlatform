using MonitorCloud.Domain.Identity;

namespace MonitorCloud.Application.Identity;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string hash, string password);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public sealed record SecretToken(string Token, string Hash);

/// <summary>Issues user access tokens and creates/hashes refresh and invitation secrets.</summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(User user, DateTimeOffset now);

    SecretToken NewRefreshToken();

    string HashRefreshToken(string token);

    SecretToken NewInvitationToken();

    string HashInvitationToken(string token);
}
