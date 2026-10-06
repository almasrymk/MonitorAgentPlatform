using Microsoft.AspNetCore.Identity;
using MonitorCloud.Application.Identity;

namespace MonitorCloud.Infrastructure.Identity;

/// <summary>ASP.NET Core Identity's PBKDF2 hasher (02 section 1).</summary>
internal sealed class PasswordHasherAdapter : Application.Identity.IPasswordHasher
{
    private static readonly object Subject = new();
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(Subject, password);

    public bool Verify(string hash, string password)
    {
        try
        {
            return _hasher.VerifyHashedPassword(Subject, hash, password) != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
