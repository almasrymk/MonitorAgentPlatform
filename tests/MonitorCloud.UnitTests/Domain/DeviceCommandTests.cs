using MonitorCloud.Domain.Commands;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared;

namespace MonitorCloud.UnitTests.Domain;

public sealed class DeviceCommandTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;

    private static DeviceCommand Create(string type = "refresh-inventory") =>
        DeviceCommand.Request(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), type, "{}", "Customer asked for it", Guid.CreateVersion7(), "Admin User", Now);

    [Fact]
    public void Request_creates_a_pending_command_with_a_5_minute_expiry_and_a_fresh_nonce()
    {
        var command = Create();
        var other = Create();

        command.Status.ShouldBe(CommandStatus.Pending);
        command.ExpiresAt.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(Now.AddMinutes(5).ToUnixTimeMilliseconds()));
        command.Nonce.Length.ShouldBe(32);
        command.Nonce.ShouldNotBe(other.Nonce);
        command.Id.Version.ShouldBe(7);
        command.DomainEvents.OfType<DeviceCommandRequestedV1>().ShouldHaveSingleItem().CommandId.ShouldBe(command.Id);
    }

    [Fact]
    public void Request_refuses_an_unknown_type() =>
        Should.Throw<DomainException>(() => Create("format-disk")).Error.Code.ShouldBe("COMMAND_TYPE_UNKNOWN");

    [Fact]
    public void Complete_records_the_first_answer_only()
    {
        var command = Create();
        command.MarkSent(Now.AddSeconds(1));

        command.Complete(CommandStatus.Succeeded, "done", Now.AddSeconds(2)).IsSuccess.ShouldBeTrue();
        command.Complete(CommandStatus.Failed, "again", Now.AddSeconds(3)).Error!.Code.ShouldBe("COMMAND_COMPLETED");

        (command.Status, command.Output, command.SentAt, command.CompletedAt).ShouldBe((CommandStatus.Succeeded, "done", Now.AddSeconds(1), Now.AddSeconds(2)));
        command.DomainEvents.OfType<DeviceCommandCompletedV1>().ShouldHaveSingleItem().Status.ShouldBe(CommandStatus.Succeeded);
    }

    [Fact]
    public void Complete_needs_a_final_status_and_cuts_long_output()
    {
        var command = Create();

        command.Complete(CommandStatus.Sent, null, Now).IsFailure.ShouldBeTrue();
        command.Complete(CommandStatus.Failed, new string('x', DeviceCommand.MaxOutputLength + 50), Now).IsSuccess.ShouldBeTrue();
        command.Output!.Length.ShouldBe(DeviceCommand.MaxOutputLength);
    }

    [Fact]
    public void An_unanswered_command_expires_one_minute_after_its_expiry_and_a_late_answer_is_still_recorded()
    {
        var command = Create();

        command.ExpireIfOverdue(Now.AddMinutes(5).AddSeconds(30)).ShouldBeFalse();
        command.ExpireIfOverdue(Now.AddMinutes(6)).ShouldBeTrue();
        command.Status.ShouldBe(CommandStatus.Expired);
        command.ExpireIfOverdue(Now.AddMinutes(7)).ShouldBeFalse();

        command.Complete(CommandStatus.Expired, "The command has expired.", Now.AddMinutes(7)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_signing_payload_is_the_canonical_text_of_05_section_9()
    {
        var command = Create();

        command.SigningPayload().ShouldBe(string.Join('|', command.Id.ToString("N"), "refresh-inventory", "{}", command.ExpiresAt.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture), command.Nonce, command.DeviceId.ToString("N")));
    }
}
