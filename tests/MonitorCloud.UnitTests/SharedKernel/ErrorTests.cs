using MonitorCloud.SharedKernel;

namespace MonitorCloud.UnitTests.SharedKernel;

public sealed class ErrorTests
{
    public static TheoryData<Func<string, string, Error>, ErrorKind> Factories => new()
    {
        { Error.Validation, ErrorKind.Validation },
        { Error.NotFound, ErrorKind.NotFound },
        { Error.Conflict, ErrorKind.Conflict },
        { Error.Forbidden, ErrorKind.Forbidden },
        { Error.Unauthorized, ErrorKind.Unauthorized },
        { Error.Locked, ErrorKind.Locked },
        { Error.TooManyRequests, ErrorKind.TooManyRequests },
        { Error.Gone, ErrorKind.Gone },
        { Error.Unavailable, ErrorKind.Unavailable },
        { Error.PayloadTooLarge, ErrorKind.PayloadTooLarge },
        { Error.UnsupportedMediaType, ErrorKind.UnsupportedMediaType },
    };

    [Theory]
    [MemberData(nameof(Factories))]
    public void Factory_sets_code_message_and_kind(Func<string, string, Error> factory, ErrorKind kind)
    {
        var error = factory("AREA_REASON", "message");

        error.Code.ShouldBe("AREA_REASON");
        error.Message.ShouldBe("message");
        error.Kind.ShouldBe(kind);
    }

    [Fact]
    public void Errors_with_the_same_values_are_equal() =>
        Error.Conflict("X_Y", "m").ShouldBe(Error.Conflict("X_Y", "m"));

    [Fact]
    public void Domain_exception_carries_the_error()
    {
        var error = Error.Conflict("LOCATION_DEFAULT", "The default location cannot be deleted.");

        var exception = new DomainException(error);

        exception.Error.ShouldBe(error);
        exception.Message.ShouldBe(error.Message);
    }
}
