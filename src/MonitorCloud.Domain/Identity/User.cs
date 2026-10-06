using MonitorCloud.SharedKernel;

namespace MonitorCloud.Domain.Identity;

public enum UserStatus
{
    Invited,
    Active,
    Inactive,
}

/// <summary>A person who signs in: platform staff (no tenant) or a customer user (02 section 1).</summary>
public sealed class User : AggregateRoot, IOptionallyTenantOwned
{
    public const int MaxFailedSignIns = 5;
    public const int EmailMaxLength = 256;
    public const int FullNameMaxLength = 200;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);
    public static readonly IReadOnlyList<string> Languages = ["en", "ar"];

    private List<Guid> _locationScope = [];

    private User()
    {
        Email = string.Empty;
        FullName = string.Empty;
        Role = string.Empty;
        PreferredLanguage = "en";
    }

    public Guid? TenantId { get; private set; }
    public string Email { get; private set; }
    public string FullName { get; private set; }
    public string? PasswordHash { get; private set; }
    public string Role { get; private set; }
    public UserStatus Status { get; private set; }
    public IReadOnlyCollection<Guid> LocationScope => _locationScope.AsReadOnly();
    public string PreferredLanguage { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockoutEndsAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? InvitationTokenHash { get; private set; }
    public DateTimeOffset? InvitationExpiresAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public bool IsPlatform => TenantId is null;
    public bool IsAdministrator => Role == Roles.Administrator && Status == UserStatus.Active;

    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static User CreatePlatformUser(string email, string fullName, string role, string passwordHash, DateTimeOffset now)
    {
        Guard.Against(!Roles.IsPlatformRole(role), IdentityErrors.RoleNotAllowed);
        var user = New(null, email, fullName, role, [], now);
        user.PasswordHash = Guard.NotEmpty(passwordHash, nameof(PasswordHash), 500);
        user.Status = UserStatus.Active;
        return user;
    }

    /// <summary>A customer user with a password (seeding and administrative creation).</summary>
    public static User CreateTenantUser(Guid tenantId, string email, string fullName, string role, IEnumerable<Guid> locationScope, string passwordHash, DateTimeOffset now)
    {
        var user = NewTenantUser(tenantId, email, fullName, role, locationScope, now);
        user.PasswordHash = Guard.NotEmpty(passwordHash, nameof(PasswordHash), 500);
        user.Status = UserStatus.Active;
        return user;
    }

    /// <summary>A customer user who sets the password through the e-mailed invitation.</summary>
    public static User Invite(Guid tenantId, string email, string fullName, string role, IEnumerable<Guid> locationScope, string invitationTokenHash, DateTimeOffset now)
    {
        var user = NewTenantUser(tenantId, email, fullName, role, locationScope, now);
        user.Status = UserStatus.Invited;
        user.SetInvitation(invitationTokenHash, now);
        user.Raise(new UserInvitedV1(user.Id, tenantId, user.Email, now));
        return user;
    }

    private static User NewTenantUser(Guid tenantId, string email, string fullName, string role, IEnumerable<Guid> locationScope, DateTimeOffset now)
    {
        Guard.NotEmpty(tenantId, nameof(TenantId));
        Guard.Against(!Roles.IsTenantRole(role), IdentityErrors.RoleNotAllowed);
        return New(tenantId, email, fullName, role, locationScope, now);
    }

    private static User New(Guid? tenantId, string email, string fullName, string role, IEnumerable<Guid> locationScope, DateTimeOffset now)
    {
        var normalized = NormalizeEmail(email);
        Guard.NotEmpty(normalized, nameof(Email), EmailMaxLength);
        Guard.Against(!normalized.Contains('@', StringComparison.Ordinal), Error.Validation(Guard.ValidationCode, "Email is invalid."));
        return new User
        {
            TenantId = tenantId,
            Email = normalized,
            FullName = Guard.NotEmpty(fullName, nameof(FullName), FullNameMaxLength),
            Role = role,
            _locationScope = locationScope.Distinct().ToList(),
            CreatedAt = now,
        };
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEndsAt is { } end && end > now;

    public TimeSpan LockoutRemaining(DateTimeOffset now) => LockoutEndsAt is { } end && end > now ? end - now : TimeSpan.Zero;

    /// <summary>Counts a failed sign-in; the fifth consecutive failure locks the account for 15 minutes.</summary>
    public void RegisterFailedSignIn(DateTimeOffset now)
    {
        if (LockoutEndsAt is { } end && end <= now)
        {
            LockoutEndsAt = null;
            FailedLoginCount = 0;
        }

        FailedLoginCount++;
        if (FailedLoginCount >= MaxFailedSignIns)
            LockoutEndsAt = now.Add(LockoutDuration);
    }

    public void RegisterSuccessfulSignIn(DateTimeOffset now)
    {
        FailedLoginCount = 0;
        LockoutEndsAt = null;
        LastLoginAt = now;
    }

    public bool CanSignIn(DateTimeOffset now) => Status == UserStatus.Active && PasswordHash is not null && !IsLockedOut(now);

    public void ChangePassword(string passwordHash) =>
        PasswordHash = Guard.NotEmpty(passwordHash, nameof(PasswordHash), 500);

    public void SetLanguage(string language)
    {
        Guard.Against(!Languages.Contains(language), Error.Validation(Guard.ValidationCode, "Language must be 'en' or 'ar'."));
        PreferredLanguage = language;
    }

    public void UpdateProfile(string fullName) => FullName = Guard.NotEmpty(fullName, nameof(FullName), FullNameMaxLength);

    /// <summary>Changes role and location scope. <paramref name="otherActiveAdministrators"/> enforces the last-administrator rule.</summary>
    public void ChangeRole(string role, IEnumerable<Guid> locationScope, int otherActiveAdministrators)
    {
        if (IsPlatform)
        {
            Guard.Against(!Roles.IsPlatformRole(role), IdentityErrors.RoleNotAllowed);
        }
        else
        {
            Guard.Against(!Roles.IsTenantRole(role), IdentityErrors.RoleNotAllowed);
            Guard.Against(IsAdministrator && role != Roles.Administrator && otherActiveAdministrators == 0, IdentityErrors.LastAdministrator);
        }

        Role = role;
        _locationScope = IsPlatform ? [] : locationScope.Distinct().ToList();
    }

    public void Deactivate(int otherActiveAdministrators)
    {
        Guard.Against(Status == UserStatus.Inactive, IdentityErrors.InvalidTransition);
        Guard.Against(!IsPlatform && IsAdministrator && otherActiveAdministrators == 0, IdentityErrors.LastAdministrator);
        Status = UserStatus.Inactive;
    }

    public void Activate()
    {
        Guard.Against(Status != UserStatus.Inactive, IdentityErrors.InvalidTransition);
        Status = PasswordHash is null ? UserStatus.Invited : UserStatus.Active;
    }

    /// <summary>Starts (or restarts) the invitation; the previous token stops working.</summary>
    public void SetInvitation(string invitationTokenHash, DateTimeOffset now)
    {
        Guard.Against(Status != UserStatus.Invited, IdentityErrors.InvalidTransition);
        InvitationTokenHash = Guard.NotEmpty(invitationTokenHash, nameof(InvitationTokenHash), 128);
        InvitationExpiresAt = now.Add(InvitationLifetime);
    }

    public void AcceptInvitation(string invitationTokenHash, string passwordHash, DateTimeOffset now)
    {
        var valid = Status == UserStatus.Invited
            && InvitationTokenHash is not null
            && string.Equals(InvitationTokenHash, invitationTokenHash, StringComparison.Ordinal)
            && InvitationExpiresAt > now;
        Guard.Against(!valid, IdentityErrors.InvitationInvalid);

        PasswordHash = Guard.NotEmpty(passwordHash, nameof(PasswordHash), 500);
        Status = UserStatus.Active;
        InvitationTokenHash = null;
        InvitationExpiresAt = null;
    }
}

public sealed record UserInvitedV1(Guid UserId, Guid TenantId, string Email, DateTimeOffset At) : DomainEvent(At);
