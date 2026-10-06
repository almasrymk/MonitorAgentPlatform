using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Context;
using MonitorCloud.Application.Abstractions.Messaging;
using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Domain.Identity;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.Application.Identity.Commands.SetLanguage;

[AllowAuthenticatedUser]
public sealed record SetLanguageCommand(string Language) : ICommand;

internal sealed class SetLanguageCommandValidator : AbstractValidator<SetLanguageCommand>
{
    public SetLanguageCommandValidator() => RuleFor(x => x.Language).Must(l => User.Languages.Contains(l)).WithMessage("Language must be 'en' or 'ar'.");
}

internal sealed class SetLanguageCommandHandler(IAppDbContext db, ICurrentUser caller) : ICommandHandler<SetLanguageCommand>
{
    public async Task<Result> Handle(SetLanguageCommand request, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleOrDefaultAsync(u => u.Id == caller.UserId, cancellationToken);
        if (user is null)
            return IdentityErrors.UserNotFound;
        user.SetLanguage(request.Language);
        return Result.Success();
    }
}
