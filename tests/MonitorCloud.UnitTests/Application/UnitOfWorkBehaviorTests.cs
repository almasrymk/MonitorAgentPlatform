using MonitorCloud.Application.Abstractions.Persistence;
using MonitorCloud.Application.Behaviors;
using MonitorCloud.SharedKernel;
using NSubstitute;

namespace MonitorCloud.UnitTests.Application;

public sealed class UnitOfWorkBehaviorTests
{
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Successful_command_saves_once()
    {
        var behavior = new UnitOfWorkBehavior<RenameDeviceCommand, Result<string>>(_unitOfWork);

        var response = await behavior.Handle(new RenameDeviceCommand("a"), _ => Task.FromResult(Result<string>.Ok("a")), CancellationToken.None);

        response.IsSuccess.ShouldBeTrue();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _unitOfWork.DidNotReceive().DiscardChanges();
    }

    [Fact]
    public async Task Failed_command_saves_nothing_and_discards_changes()
    {
        var behavior = new UnitOfWorkBehavior<RetireDeviceCommand, Result>(_unitOfWork);

        var response = await behavior.Handle(new RetireDeviceCommand(), _ => Task.FromResult(Result.Failure(Error.Conflict("X_Y", "no"))), CancellationToken.None);

        response.IsFailure.ShouldBeTrue();
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        _unitOfWork.Received(1).DiscardChanges();
    }

    [Fact]
    public async Task Exception_in_the_handler_rolls_back_and_propagates()
    {
        var behavior = new UnitOfWorkBehavior<RetireDeviceCommand, Result>(_unitOfWork);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            behavior.Handle(new RetireDeviceCommand(), _ => throw new InvalidOperationException("bug"), CancellationToken.None));

        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        _unitOfWork.Received(1).DiscardChanges();
    }

    [Fact]
    public async Task Queries_never_save()
    {
        var behavior = new UnitOfWorkBehavior<ReadDevicesQuery, Result<string>>(_unitOfWork);

        await behavior.Handle(new ReadDevicesQuery("x"), _ => Task.FromResult(Result<string>.Ok("x")), CancellationToken.None);

        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
