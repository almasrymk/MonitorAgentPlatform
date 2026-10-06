namespace MonitorCloud.SharedKernel;

/// <summary>Argument checks for domain methods. Failures throw <see cref="DomainException"/> with <c>VALIDATION_FAILED</c>.</summary>
public static class Guard
{
    public const string ValidationCode = "VALIDATION_FAILED";

    public static string NotEmpty(string? value, string field, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Fail($"{field} is required.");
        value = value.Trim();
        if (value.Length > maxLength)
            throw Fail($"{field} must be at most {maxLength} characters.");
        return value;
    }

    public static string? MaxLength(string? value, string field, int maxLength)
    {
        if (value is null)
            return null;
        value = value.Trim();
        if (value.Length > maxLength)
            throw Fail($"{field} must be at most {maxLength} characters.");
        return value.Length == 0 ? null : value;
    }

    public static Guid NotEmpty(Guid value, string field)
    {
        if (value == Guid.Empty)
            throw Fail($"{field} is required.");
        return value;
    }

    public static int InRange(int value, string field, int min, int max)
    {
        if (value < min || value > max)
            throw Fail($"{field} must be between {min} and {max}.");
        return value;
    }

    public static void Against(bool condition, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (condition)
            throw new DomainException(error);
    }

    private static DomainException Fail(string message) => new(Error.Validation(ValidationCode, message));
}
