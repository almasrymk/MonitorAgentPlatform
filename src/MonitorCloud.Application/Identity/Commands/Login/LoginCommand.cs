using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Messaging;

namespace MonitorCloud.Application.Identity.Commands.Login;

[AllowAnonymousRequest]
public sealed record LoginCommand(string Email, string Password) : ICommand<AuthResultDto>;
