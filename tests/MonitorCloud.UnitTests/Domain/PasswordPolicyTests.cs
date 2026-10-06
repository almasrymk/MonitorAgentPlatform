using MonitorCloud.Domain.Identity;

namespace MonitorCloud.UnitTests.Domain;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData(null, "at least 10")]
    [InlineData("Ab1#short", "at least 10")]
    [InlineData("lowercase#123", "upper-case")]
    [InlineData("UPPERCASE#123", "lower-case")]
    [InlineData("NoDigits#Here", "digit")]
    [InlineData("NoSymbol1234", "symbol")]
    public void Weak_passwords_are_rejected_with_the_broken_rule(string? password, string reason) =>
        PasswordPolicy.Check(password, "user@alpha.test").ShouldNotBeNull().ShouldContain(reason);

    [Fact]
    public void Password_equal_to_the_email_is_rejected() =>
        PasswordPolicy.Check("User@Alpha.test1", "user@alpha.test1").ShouldNotBeNull().ShouldContain("e-mail");

    [Theory]
    [InlineData("Admin@12345")]
    [InlineData("Demo@12345")]
    [InlineData("Correct-Horse#9")]
    public void Strong_passwords_pass(string password) => PasswordPolicy.Check(password, "user@alpha.test").ShouldBeNull();
}
