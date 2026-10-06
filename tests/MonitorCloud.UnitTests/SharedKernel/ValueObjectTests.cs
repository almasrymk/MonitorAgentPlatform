using MonitorCloud.SharedKernel;

namespace MonitorCloud.UnitTests.SharedKernel;

public sealed class ValueObjectTests
{
    private sealed class Money(decimal amount, string currency) : ValueObject
    {
        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return amount;
            yield return currency;
        }
    }

    private sealed class Other(decimal amount, string currency) : ValueObject
    {
        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return amount;
            yield return currency;
        }
    }

    [Fact]
    public void Equal_components_mean_equal_values()
    {
        var a = new Money(10, "EGP");
        var b = new Money(10, "EGP");

        a.ShouldBe(b);
        (a == b).ShouldBeTrue();
        (a != b).ShouldBeFalse();
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Different_components_mean_different_values()
    {
        (new Money(10, "EGP") == new Money(11, "EGP")).ShouldBeFalse();
        (new Money(10, "EGP") != new Money(10, "USD")).ShouldBeTrue();
    }

    [Fact]
    public void Different_types_are_never_equal() =>
        new Money(10, "EGP").Equals(new Other(10, "EGP")).ShouldBeFalse();
}
