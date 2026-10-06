using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared;

namespace MonitorCloud.UnitTests.Domain;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private static User TenantUser(string role = Roles.Technician, params Guid[] locations) =>
        User.CreateTenantUser(Tenant, "  Person@Alpha.TEST ", "Sample Person", role, locations, "hash", Now);

    [Fact]
    public void Email_is_trimmed_and_lower_cased() => TenantUser().Email.ShouldBe("person@alpha.test");

    [Fact]
    public void Invalid_email_is_rejected() =>
        Should.Throw<DomainException>(() => User.CreateTenantUser(Tenant, "not-an-email", "X", Roles.Technician, [], "hash", Now));

    [Fact]
    public void Tenant_users_cannot_have_platform_roles() =>
        Should.Throw<DomainException>(() => TenantUser(Roles.PlatformAdmin)).Error.Code.ShouldBe("USER_ROLE_NOT_ALLOWED");

    [Fact]
    public void Platform_users_cannot_have_tenant_roles() =>
        Should.Throw<DomainException>(() => User.CreatePlatformUser("p@monitor.local", "P", Roles.Administrator, "hash", Now));

    [Fact]
    public void Platform_user_has_no_tenant()
    {
        var user = User.CreatePlatformUser("p@monitor.local", "P", Roles.PlatformSupport, "hash", Now);

        user.TenantId.ShouldBeNull();
        user.IsPlatform.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public void Location_scope_is_kept_without_duplicates()
    {
        var location = Guid.CreateVersion7();

        TenantUser(Roles.ITManager, location, location).LocationScope.ShouldBe([location]);
    }

    [Fact]
    public void Five_failures_lock_for_fifteen_minutes()
    {
        var user = TenantUser();

        for (var i = 0; i < 4; i++)
            user.RegisterFailedSignIn(Now);
        user.IsLockedOut(Now).ShouldBeFalse();

        user.RegisterFailedSignIn(Now);
        user.IsLockedOut(Now).ShouldBeTrue();
        user.LockoutRemaining(Now).ShouldBe(TimeSpan.FromMinutes(15));
        user.CanSignIn(Now).ShouldBeFalse();
        user.IsLockedOut(Now.AddMinutes(15)).ShouldBeFalse();
        user.CanSignIn(Now.AddMinutes(15)).ShouldBeTrue();
    }

    [Fact]
    public void A_failure_after_the_lockout_starts_counting_again()
    {
        var user = TenantUser();
        for (var i = 0; i < 5; i++)
            user.RegisterFailedSignIn(Now);

        user.RegisterFailedSignIn(Now.AddMinutes(16));

        user.FailedLoginCount.ShouldBe(1);
        user.IsLockedOut(Now.AddMinutes(16)).ShouldBeFalse();
    }

    [Fact]
    public void Success_resets_the_counter_and_records_the_time()
    {
        var user = TenantUser();
        user.RegisterFailedSignIn(Now);

        user.RegisterSuccessfulSignIn(Now.AddMinutes(1));

        user.FailedLoginCount.ShouldBe(0);
        user.LastLoginAt.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public void Invitation_is_accepted_once_with_the_right_token_before_expiry()
    {
        var user = User.Invite(Tenant, "new@alpha.test", "New", Roles.ReportViewer, [], "token-hash", Now);
        user.Status.ShouldBe(UserStatus.Invited);
        user.CanSignIn(Now).ShouldBeFalse();
        user.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<UserInvitedV1>();

        Should.Throw<DomainException>(() => user.AcceptInvitation("other-hash", "pw-hash", Now)).Error.Code.ShouldBe("AUTH_INVITATION_INVALID");
        user.AcceptInvitation("token-hash", "pw-hash", Now.AddDays(1));

        user.Status.ShouldBe(UserStatus.Active);
        user.InvitationTokenHash.ShouldBeNull();
        Should.Throw<DomainException>(() => user.AcceptInvitation("token-hash", "pw-hash", Now.AddDays(1)));
    }

    [Fact]
    public void Invitation_expires_after_seven_days()
    {
        var user = User.Invite(Tenant, "new@alpha.test", "New", Roles.ReportViewer, [], "token-hash", Now);

        Should.Throw<DomainException>(() => user.AcceptInvitation("token-hash", "pw-hash", Now.AddDays(7)));
    }

    [Fact]
    public void Resending_replaces_the_invitation()
    {
        var user = User.Invite(Tenant, "new@alpha.test", "New", Roles.ReportViewer, [], "first", Now);

        user.SetInvitation("second", Now.AddDays(6));

        Should.Throw<DomainException>(() => user.AcceptInvitation("first", "pw", Now.AddDays(7)));
        user.AcceptInvitation("second", "pw", Now.AddDays(12));
    }

    [Fact]
    public void Active_users_cannot_get_a_new_invitation() =>
        Should.Throw<DomainException>(() => TenantUser().SetInvitation("x", Now)).Error.Code.ShouldBe("USER_INVALID_TRANSITION");

    [Fact]
    public void The_last_administrator_cannot_be_demoted_or_deactivated()
    {
        var admin = TenantUser(Roles.Administrator);

        Should.Throw<DomainException>(() => admin.ChangeRole(Roles.Technician, [], 0)).Error.Code.ShouldBe("USER_LAST_ADMIN");
        Should.Throw<DomainException>(() => admin.Deactivate(0)).Error.Code.ShouldBe("USER_LAST_ADMIN");

        admin.ChangeRole(Roles.Technician, [], 1);
        admin.Role.ShouldBe(Roles.Technician);
    }

    [Fact]
    public void Deactivate_and_activate_follow_the_transitions()
    {
        var user = TenantUser();

        user.Deactivate(0);
        user.Status.ShouldBe(UserStatus.Inactive);
        user.CanSignIn(Now).ShouldBeFalse();
        Should.Throw<DomainException>(() => user.Deactivate(0));

        user.Activate();
        user.Status.ShouldBe(UserStatus.Active);
        Should.Throw<DomainException>(() => user.Activate());
    }

    [Fact]
    public void Activating_an_invited_user_who_never_set_a_password_returns_to_invited()
    {
        var user = User.Invite(Tenant, "new@alpha.test", "New", Roles.ReportViewer, [], "token", Now);
        user.Deactivate(1);

        user.Activate();

        user.Status.ShouldBe(UserStatus.Invited);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public void Supported_languages_are_accepted(string language)
    {
        var user = TenantUser();

        user.SetLanguage(language);

        user.PreferredLanguage.ShouldBe(language);
    }

    [Fact]
    public void Unsupported_language_is_rejected() => Should.Throw<DomainException>(() => TenantUser().SetLanguage("fr"));

    [Fact]
    public void Changing_role_replaces_the_location_scope()
    {
        var user = TenantUser(Roles.ITManager, Guid.CreateVersion7());
        var other = Guid.CreateVersion7();

        user.ChangeRole(Roles.Technician, [other], 1);

        user.LocationScope.ShouldBe([other]);
    }

    [Fact]
    public void Profile_and_password_can_change()
    {
        var user = TenantUser();

        user.UpdateProfile(" Renamed ");
        user.ChangePassword("new-hash");

        user.FullName.ShouldBe("Renamed");
        user.PasswordHash.ShouldBe("new-hash");
    }
}
