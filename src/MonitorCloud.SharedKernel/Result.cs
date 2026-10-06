namespace MonitorCloud.SharedKernel;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    Locked,
    TooManyRequests,
    Gone,
    Unavailable,
}

/// <summary>A stable, client-facing error. <see cref="Code"/> values (<c>AREA_REASON</c>) are part of the public API contract.</summary>
public sealed record Error(string Code, string Message, ErrorKind Kind)
{
    /// <summary>Field errors of a validation failure (camelCase field names).</summary>
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; init; }

    /// <summary>Extra members written into the problem body (e.g. <c>retryAfterSeconds</c>, <c>feature</c>).</summary>
    public IReadOnlyDictionary<string, object?>? Details { get; init; }

    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);
    public static Error Locked(string code, string message) => new(code, message, ErrorKind.Locked);
    public static Error TooManyRequests(string code, string message) => new(code, message, ErrorKind.TooManyRequests);
    public static Error Gone(string code, string message) => new(code, message, ErrorKind.Gone);
    public static Error Unavailable(string code, string message) => new(code, message, ErrorKind.Unavailable);

    public Error With(string key, object? value)
    {
        var details = Details is null ? new Dictionary<string, object?>(StringComparer.Ordinal) : new Dictionary<string, object?>(Details, StringComparer.Ordinal);
        details[key] = value;
        return this with { Details = details };
    }
}

public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(null);
    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(error);
    }

    public static Result<T> Success<T>(T value) => Result<T>.Ok(value);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error) : base(error) => _value = value;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Result failed: {Error!.Code}");

    public static Result<T> Ok(T value) => new(value, null);
    public static new Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, error);
    }

    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}

/// <summary>Thrown when an aggregate invariant is violated. Mapped to problem details at the edge.</summary>
public sealed class DomainException(Error error) : Exception(error.Message)
{
    public Error Error { get; } = error;
}
