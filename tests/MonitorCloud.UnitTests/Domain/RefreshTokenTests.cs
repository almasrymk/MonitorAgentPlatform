using MonitorCloud.Domain.Identity;
using MonitorCloud.TestShared;

namespace MonitorCloud.UnitTests.Domain;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;

    [Fact]
    public void New_token_is_active_for_fourteen_days()
    {
        var token = RefreshToken.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), "hash", "192.0.2.1", Now);

        token.IsActive(Now).ShouldBeTrue();
        token.IsActive(Now.AddDays(14)).ShouldBeFalse();
        token.IsReuse.ShouldBeFalse();
    }

    [Fact]
    public void Rotation_revokes_and_links_the_replacement()
    {
        var token = RefreshToken.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), "hash", null, Now);
        var replacement = Guid.CreateVersion7();

        token.Rotate(replacement, Now.AddMinutes(5));

        token.IsActive(Now.AddMinutes(5)).ShouldBeFalse();
        token.ReplacedById.ShouldBe(replacement);
        token.IsReuse.ShouldBeTrue();
    }

    [Fact]
    public void Revoke_keeps_the_first_revocation_time()
    {
        var token = RefreshToken.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), "hash", null, Now);

        token.Revoke(Now.AddMinutes(1));
        token.Revoke(Now.AddMinutes(2));

        token.RevokedAt.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public void Long_ip_addresses_are_truncated() =>
        RefreshToken.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), "hash", new string('1', 80), Now).CreatedIp!.Length.ShouldBe(64);
}
