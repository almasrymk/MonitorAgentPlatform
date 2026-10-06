using MonitorCloud.SharedKernel;

namespace MonitorCloud.UnitTests.SharedKernel;

public sealed class GuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NotEmpty_rejects_blank_strings(string? value)
    {
        var ex = Should.Throw<DomainException>(() => Guard.NotEmpty(value, "Name"));

        ex.Error.Code.ShouldBe(Guard.ValidationCode);
        ex.Error.Kind.ShouldBe(ErrorKind.Validation);
        ex.Error.Message.ShouldContain("Name");
    }

    [Fact]
    public void NotEmpty_trims_the_value() => Guard.NotEmpty("  Cairo HQ ", "Name").ShouldBe("Cairo HQ");

    [Fact]
    public void NotEmpty_rejects_values_longer_than_the_maximum() =>
        Should.Throw<DomainException>(() => Guard.NotEmpty(new string('x', 11), "Code", 10));

    [Fact]
    public void NotEmpty_accepts_the_maximum_length() => Guard.NotEmpty(new string('x', 10), "Code", 10).Length.ShouldBe(10);

    [Fact]
    public void MaxLength_allows_null_and_turns_blank_into_null()
    {
        Guard.MaxLength(null, "City", 5).ShouldBeNull();
        Guard.MaxLength("   ", "City", 5).ShouldBeNull();
        Guard.MaxLength(" Giza ", "City", 5).ShouldBe("Giza");
    }

    [Fact]
    public void MaxLength_rejects_long_values() => Should.Throw<DomainException>(() => Guard.MaxLength("Alexandria", "City", 5));

    [Fact]
    public void NotEmpty_guid_rejects_empty() => Should.Throw<DomainException>(() => Guard.NotEmpty(Guid.Empty, "TenantId"));

    [Fact]
    public void NotEmpty_guid_returns_the_value()
    {
        var id = Guid.CreateVersion7();
        Guard.NotEmpty(id, "TenantId").ShouldBe(id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void InRange_rejects_values_outside_the_range(int value) =>
        Should.Throw<DomainException>(() => Guard.InRange(value, "Interval", 1, 10));

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void InRange_accepts_the_bounds(int value) => Guard.InRange(value, "Interval", 1, 10).ShouldBe(value);

    [Fact]
    public void Against_throws_the_given_error_when_the_condition_holds()
    {
        var error = Error.Conflict("USER_LAST_ADMIN", "Last administrator.");

        Should.Throw<DomainException>(() => Guard.Against(true, error)).Error.ShouldBe(error);
        Should.NotThrow(() => Guard.Against(false, error));
    }
}
