namespace MonitorCloud.Domain.Identity;

/// <summary>10+ characters with upper, lower, digit and symbol; not equal to the e-mail (03 section 1).</summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 10;

    /// <summary>Returns the first broken rule, or null when the password is acceptable.</summary>
    public static string? Check(string? password, string? email)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
            return $"The password must have at least {MinimumLength} characters.";
        if (!password.Any(char.IsUpper))
            return "The password must contain an upper-case letter.";
        if (!password.Any(char.IsLower))
            return "The password must contain a lower-case letter.";
        if (!password.Any(char.IsDigit))
            return "The password must contain a digit.";
        if (password.All(char.IsLetterOrDigit))
            return "The password must contain a symbol.";
        if (email is not null && string.Equals(password.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase))
            return "The password must not be the e-mail address.";
        return null;
    }
}
