using MonitorCloud.Infrastructure.Messaging;
using MonitorCloud.TestShared;

namespace MonitorCloud.UnitTests.Infrastructure;

public sealed class OutboxMessageTests
{
    private static readonly DateTimeOffset Now = TestClock.DefaultStart;

    private static OutboxMessage NewMessage() => OutboxMessage.Create(Guid.CreateVersion7(), "T", "{}", null, Now);

    [Fact]
    public void New_message_is_due_immediately()
    {
        var message = NewMessage();

        message.NextAttemptAt.ShouldBe(Now);
        message.Attempts.ShouldBe(0);
        message.ProcessedAt.ShouldBeNull();
    }

    [Fact]
    public void Failures_back_off_exponentially()
    {
        var message = NewMessage();

        message.MarkFailed("boom", Now);
        message.NextAttemptAt.ShouldBe(Now.AddSeconds(2));
        message.MarkFailed("boom", Now);
        message.NextAttemptAt.ShouldBe(Now.AddSeconds(4));
        message.MarkFailed("boom", Now);
        message.NextAttemptAt.ShouldBe(Now.AddSeconds(8));
        message.LastError.ShouldBe("boom");
    }

    [Fact]
    public void Message_is_dead_lettered_after_eight_attempts()
    {
        var message = NewMessage();

        for (var i = 0; i < OutboxMessage.MaxAttempts - 1; i++)
            message.MarkFailed("boom", Now);
        message.DeadLettered.ShouldBeFalse();

        message.MarkFailed("boom", Now);
        message.DeadLettered.ShouldBeTrue();
        message.Attempts.ShouldBe(8);
    }

    [Fact]
    public void Long_errors_are_truncated() =>
        NewMessage().Also(m => m.MarkFailed(new string('x', 5000), Now)).LastError!.Length.ShouldBe(2000);

    [Fact]
    public void Processing_clears_the_last_error()
    {
        var message = NewMessage();
        message.MarkFailed("boom", Now);

        message.MarkProcessed(Now.AddSeconds(3));

        message.ProcessedAt.ShouldBe(Now.AddSeconds(3));
        message.LastError.ShouldBeNull();
    }
}

internal static class TestExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
