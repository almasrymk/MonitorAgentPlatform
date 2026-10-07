using System.Security.Cryptography;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MonitorCloud.Application.Abstractions.Audit;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Devices.Contracts;
using MonitorCloud.Domain.Identity;
using MonitorCloud.Domain.Tenancy;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Tenancy;

public sealed record EnrollmentCodeCreatedDto(Guid Id, string Code, DateTimeOffset ExpiresAt, int? MaxUses, InstallCommandsDto InstallCommands);

public sealed record InstallCommandsDto(string Windows, string Linux, string MacOs);

public sealed record EnrollmentCodeDto(Guid Id, string CodePrefix, DateTimeOffset ExpiresAt, int? MaxUses, int Uses, bool Revoked, bool Usable, DateTimeOffset CreatedAt);

/// <summary>Location enrollment codes: <c>LOC-XXXXXX-XXXXXX</c>, 60 bits, shown once (02 section 2, MC-302).</summary>
public static class EnrollmentCodes
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string New()
    {
        var bytes = RandomNumberGenerator.GetBytes(12);
        var chars = bytes.Select(b => Alphabet[b % Alphabet.Length]).ToArray();
        return $"LOC-{new string(chars, 0, 6)}-{new string(chars, 6, 6)}";
    }

    public static string Normalize(string code) => new((code ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Normalize(code))));

    public static string Prefix(string code) => Normalize(code)[..Math.Min(8, Normalize(code).Length)];

    /// <summary>Install commands per OS (MC-302); the agent installers accept PRODUCTKEY, LOCATION and CLOUDURL (AG-11).</summary>
    public static InstallCommandsDto InstallCommands(string code, string cloudUrl) => new(
        $"msiexec /i MonitorAgent.msi PRODUCTKEY=\"<product key>\" LOCATION=\"{code}\" CLOUDURL=\"{cloudUrl}\" /qn",
        $"curl -fsSL {cloudUrl.TrimEnd('/')}/install/monitor-agent.sh | sudo MONITORAGENT_PRODUCTKEY=\"<product key>\" MONITORAGENT_LOCATION=\"{code}\" MONITORAGENT_CLOUDURL=\"{cloudUrl}\" bash",
        $"sudo MONITORAGENT_PRODUCTKEY=\"<product key>\" MONITORAGENT_LOCATION=\"{code}\" MONITORAGENT_CLOUDURL=\"{cloudUrl}\" installer -pkg MonitorAgent.pkg -target /");
}

[RequirePermission(Permissions.DevicesEnroll)]
public sealed record CreateEnrollmentCodeCommand(Guid LocationId, int ExpiresInHours, int? MaxUses) : ICommand<EnrollmentCodeCreatedDto>;

internal sealed class CreateEnrollmentCodeCommandValidator : AbstractValidator<CreateEnrollmentCodeCommand>
{
    public CreateEnrollmentCodeCommandValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.ExpiresInHours).InclusiveBetween(1, 720);
        RuleFor(x => x.MaxUses).GreaterThan(0).When(x => x.MaxUses is not null);
    }
}

internal sealed class CreateEnrollmentCodeCommandHandler(
    IAppDbContext db, IUnitOfWork unitOfWork, ITenantContext scope, ICurrentUser caller, IAuditLogger audit, IOptions<AgentSettings> agent, TimeProvider clock)
    : ICommandHandler<CreateEnrollmentCodeCommand, EnrollmentCodeCreatedDto>
{
    public async Task<Result<EnrollmentCodeCreatedDto>> Handle(CreateEnrollmentCodeCommand request, CancellationToken cancellationToken)
    {
        var location = await db.Set<Location>().SingleOrDefaultAsync(l => l.Id == request.LocationId, cancellationToken);
        if (location is null)
            return TenancyErrors.LocationNotFound;
        var plain = EnrollmentCodes.New();
        var code = LocationEnrollmentCode.Create(scope.TenantId!.Value, location.Id, EnrollmentCodes.Hash(plain), EnrollmentCodes.Prefix(plain),
            TimeSpan.FromHours(request.ExpiresInHours), request.MaxUses, caller.UserId, clock.GetUtcNow());
        db.Set<LocationEnrollmentCode>().Add(code);
        audit.Add("enrollment_code.created", "Location", location.Id.ToString(), $"{code.CodePrefix}… expires {code.ExpiresAt:O}");
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new EnrollmentCodeCreatedDto(code.Id, plain, code.ExpiresAt, code.MaxUses, EnrollmentCodes.InstallCommands(plain, agent.Value.PublicBaseUrl));
    }
}

[RequirePermission(Permissions.DevicesEnroll)]
public sealed record GetEnrollmentCodesQuery(Guid LocationId) : IQuery<IReadOnlyList<EnrollmentCodeDto>>;

internal sealed class GetEnrollmentCodesQueryHandler(IReadDbContext db, TimeProvider clock) : IQueryHandler<GetEnrollmentCodesQuery, IReadOnlyList<EnrollmentCodeDto>>
{
    public async Task<Result<IReadOnlyList<EnrollmentCodeDto>>> Handle(GetEnrollmentCodesQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Query<Location>().AnyAsync(l => l.Id == request.LocationId, cancellationToken))
            return TenancyErrors.LocationNotFound;
        var now = clock.GetUtcNow();
        var codes = await db.Query<LocationEnrollmentCode>().Where(c => c.LocationId == request.LocationId).OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
        IReadOnlyList<EnrollmentCodeDto> result = codes
            .Select(c => new EnrollmentCodeDto(c.Id, c.CodePrefix, c.ExpiresAt, c.MaxUses, c.Uses, c.RevokedAt is not null, c.CanBeUsed(now), c.CreatedAt))
            .ToList();
        return Result.Success(result);
    }
}

[RequirePermission(Permissions.DevicesEnroll)]
public sealed record RevokeEnrollmentCodeCommand(Guid LocationId, Guid CodeId) : ICommand;

internal sealed class RevokeEnrollmentCodeCommandValidator : AbstractValidator<RevokeEnrollmentCodeCommand>
{
    public RevokeEnrollmentCodeCommandValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.CodeId).NotEmpty();
    }
}

internal sealed class RevokeEnrollmentCodeCommandHandler(IAppDbContext db, IAuditLogger audit, TimeProvider clock) : ICommandHandler<RevokeEnrollmentCodeCommand>
{
    public async Task<Result> Handle(RevokeEnrollmentCodeCommand request, CancellationToken cancellationToken)
    {
        var code = await db.Set<LocationEnrollmentCode>().SingleOrDefaultAsync(c => c.Id == request.CodeId && c.LocationId == request.LocationId, cancellationToken);
        if (code is null)
            return Error.NotFound("ENROLLMENT_CODE_NOT_FOUND", "Enrollment code not found.");
        code.Revoke(clock.GetUtcNow());
        audit.Add("enrollment_code.revoked", "Location", request.LocationId.ToString(), code.CodePrefix);
        return Result.Success();
    }
}
