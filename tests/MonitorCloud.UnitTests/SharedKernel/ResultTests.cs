using MonitorCloud.SharedKernel;

namespace MonitorCloud.UnitTests.SharedKernel;

public sealed class ResultTests
{
    private static readonly Error SampleError = Error.NotFound("DEVICE_NOT_FOUND", "Device not found.");

    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Failure_carries_the_error()
    {
        var result = Result.Failure(SampleError);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SampleError);
    }

    [Fact]
    public void Failure_requires_an_error() =>
        Should.Throw<ArgumentNullException>(() => Result.Failure(null!));

    [Fact]
    public void Generic_success_exposes_the_value()
    {
        Result<int> result = 42;

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Generic_failure_throws_when_the_value_is_read()
    {
        Result<int> result = SampleError;

        result.IsFailure.ShouldBeTrue();
        Should.Throw<InvalidOperationException>(() => _ = result.Value).Message.ShouldContain("DEVICE_NOT_FOUND");
    }

    [Fact]
    public void Error_converts_implicitly_to_a_failed_result()
    {
        Result result = SampleError;

        result.Error.ShouldBe(SampleError);
    }

    [Fact]
    public void Success_factory_creates_a_generic_result() =>
        Result.Success("value").Value.ShouldBe("value");
}
