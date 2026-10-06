using Microsoft.Extensions.Logging;
using MonitorCloud.Application.Behaviors;
using MonitorCloud.SharedKernel;
using MonitorCloud.TestShared;
using MonitorCloud.TestShared.Builders;

namespace MonitorCloud.UnitTests.Application;

public sealed class LoggingBehaviorTests
{
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static async Task<ListLogger<LoggingBehavior<ReadDevicesQuery, Result<string>>>> Run(TimeSpan duration, Result<string> result)
    {
        var clock = new TestClock();
        var logger = new ListLogger<LoggingBehavior<ReadDevicesQuery, Result<string>>>();
        var behavior = new LoggingBehavior<ReadDevicesQuery, Result<string>>(logger, TestCurrentUser.Platform(), TestTenantContext.Platform(), clock);

        await behavior.Handle(new ReadDevicesQuery("secret-filter"), _ =>
        {
            clock.Advance(duration);
            return Task.FromResult(result);
        }, CancellationToken.None);
        return logger;
    }

    [Fact]
    public async Task Fast_requests_are_logged_as_information()
    {
        var logger = await Run(TimeSpan.FromMilliseconds(20), Result<string>.Ok("x"));

        logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Information);
        logger.Entries[0].Message.ShouldContain(nameof(ReadDevicesQuery));
    }

    [Fact]
    public async Task Requests_slower_than_500_ms_are_logged_as_warnings()
    {
        var logger = await Run(TimeSpan.FromMilliseconds(501), Result<string>.Ok("x"));

        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("Slow request", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failures_log_the_error_code_but_never_the_request_body()
    {
        var logger = await Run(TimeSpan.Zero, Error.NotFound("DEVICE_NOT_FOUND", "missing"));

        logger.Entries.ShouldContain(e => e.Message.Contains("DEVICE_NOT_FOUND", StringComparison.Ordinal));
        logger.Entries.ShouldAllBe(e => !e.Message.Contains("secret-filter", StringComparison.Ordinal));
    }
}
