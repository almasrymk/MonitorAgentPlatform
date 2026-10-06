using FluentValidation;
using MonitorCloud.Application.Behaviors;
using MonitorCloud.SharedKernel;

namespace MonitorCloud.UnitTests.Application;

public sealed class ValidationBehaviorTests
{
    private sealed class RenameValidator : AbstractValidator<RenameDeviceCommand>
    {
        public RenameValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
            RuleFor(x => x.Name).MaximumLength(5).WithMessage("Name is too long.");
        }
    }

    private sealed class SecondValidator : AbstractValidator<RenameDeviceCommand>
    {
        public SecondValidator() => RuleFor(x => x.Name).Must(n => !n.Contains('!', StringComparison.Ordinal)).WithMessage("No exclamation marks.");
    }

    private static async Task<(Result<string> Response, bool Called)> Run(string name, params IValidator<RenameDeviceCommand>[] validators)
    {
        var called = false;
        var behavior = new ValidationBehavior<RenameDeviceCommand, Result<string>>(validators);
        var response = await behavior.Handle(new RenameDeviceCommand(name), _ =>
        {
            called = true;
            return Task.FromResult(Result<string>.Ok(name));
        }, CancellationToken.None);
        return (response, called);
    }

    [Fact]
    public async Task Without_validators_the_handler_runs()
    {
        var (response, called) = await Run("x");

        called.ShouldBeTrue();
        response.Value.ShouldBe("x");
    }

    [Fact]
    public async Task Valid_request_reaches_the_handler()
    {
        var (response, called) = await Run("abc", new RenameValidator());

        called.ShouldBeTrue();
        response.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Invalid_request_returns_VALIDATION_FAILED_with_camel_case_field_errors()
    {
        var (response, called) = await Run("toolong!", new RenameValidator(), new SecondValidator());

        called.ShouldBeFalse();
        response.Error!.Code.ShouldBe("VALIDATION_FAILED");
        response.Error.Kind.ShouldBe(ErrorKind.Validation);
        response.Error.FieldErrors.ShouldNotBeNull();
        response.Error.FieldErrors!.Keys.ShouldBe(["name"]);
        response.Error.FieldErrors["name"].ShouldBe(["Name is too long.", "No exclamation marks."], ignoreOrder: true);
    }

    [Fact]
    public async Task Empty_name_reports_the_required_message()
    {
        var (response, _) = await Run(string.Empty, new RenameValidator());

        response.Error!.FieldErrors!["name"].ShouldContain("Name is required.");
    }
}
