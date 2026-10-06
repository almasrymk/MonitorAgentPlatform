using MonitorCloud.Application.Common;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.UnitTests.Application;

public sealed class CommonTests
{
    [Theory]
    [InlineData(null, null, 1, 20)]
    [InlineData(0, 0, 1, 1)]
    [InlineData(3, 50, 3, 50)]
    [InlineData(2, 500, 2, 200)]
    [InlineData(-5, -1, 1, 1)]
    public void Paging_is_normalized_and_capped_at_200(int? page, int? pageSize, int expectedPage, int expectedSize)
    {
        var (p, s) = Paging.Normalize(page, pageSize);

        p.ShouldBe(expectedPage);
        s.ShouldBe(expectedSize);
    }

    [Fact]
    public void ResultFactory_creates_plain_failures()
    {
        var error = Error.Forbidden("AUTH_FORBIDDEN", "no");

        var result = ResultFactory.Failure<Result>(error);

        result.ShouldBeOfType<Result>().Error.ShouldBe(error);
    }

    [Fact]
    public void ResultFactory_creates_generic_failures()
    {
        var error = Error.Forbidden("AUTH_FORBIDDEN", "no");

        var result = ResultFactory.Failure<Result<int>>(error);

        result.Error.ShouldBe(error);
        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Validation_failure_carries_field_errors()
    {
        var error = CommonErrors.ValidationFailed(new Dictionary<string, string[]> { ["email"] = ["Required."] });

        error.Code.ShouldBe("VALIDATION_FAILED");
        error.FieldErrors!["email"].ShouldBe(["Required."]);
    }

    [Fact]
    public void Feature_error_names_the_feature() =>
        CommonErrors.FeatureNotEntitled("reports.pdf").Message.ShouldContain("reports.pdf");
}
