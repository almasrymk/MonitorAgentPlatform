using FluentValidation;
using MediatR;
using MonitorCloud.Application.Abstractions.Authorization;
using MonitorCloud.Application.Abstractions.Messaging;

namespace MonitorCloud.ArchitectureTests;

/// <summary>Rule 5 of 09 section 3: one handler per request, a validator per command, an authorization attribute on every request.</summary>
public sealed class RequestConventionTests
{
    private static readonly Type[] AllTypes = Assemblies.Ours.SelectMany(a => a.GetTypes()).ToArray();

    private static IEnumerable<Type> Requests => Assemblies.Application.GetTypes()
        .Where(t => t is { IsAbstract: false, IsInterface: false } && t.GetInterfaces().Any(IsRequestInterface));

    private static bool IsRequestInterface(Type i) =>
        i == typeof(IRequest) || (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));

    private static Type ResponseOf(Type request) =>
        request.GetInterfaces().First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)).GetGenericArguments()[0];

    [Fact]
    public void Every_request_has_exactly_one_handler()
    {
        var offenders = Requests
            .Select(r => (Request: r, Handlers: AllTypes.Count(t => t is { IsAbstract: false, IsInterface: false }
                && t.GetInterfaces().Contains(typeof(IRequestHandler<,>).MakeGenericType(r, ResponseOf(r))))))
            .Where(x => x.Handlers != 1)
            .Select(x => $"{x.Request.Name}: {x.Handlers}")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Every_command_has_a_validator()
    {
        var offenders = Requests
            .Where(r => typeof(IBaseCommand).IsAssignableFrom(r))
            .Where(r => !AllTypes.Any(t => t is { IsAbstract: false } && typeof(IValidator<>).MakeGenericType(r).IsAssignableFrom(t)))
            .Select(r => r.Name)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Every_request_declares_its_authorization()
    {
        var offenders = Requests
            .Where(r => !AuthorizationAttributes.Declarations.Any(a => r.IsDefined(a, false)))
            .Select(r => r.Name)
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Requests_are_sealed_records_or_classes()
    {
        var offenders = Requests.Where(r => !r.IsSealed).Select(r => r.Name).ToList();

        offenders.ShouldBeEmpty();
    }
}
