using Microsoft.Extensions.Time.Testing;

namespace MonitorCloud.TestShared;

/// <summary>Controllable clock for tests. Starts at a fixed instant; nothing in a test depends on wall-clock time.</summary>
public sealed class TestClock : FakeTimeProvider
{
    public static readonly DateTimeOffset DefaultStart = new(2026, 10, 6, 8, 0, 0, TimeSpan.Zero);

    public TestClock() : base(DefaultStart)
    {
        AutoAdvanceAmount = TimeSpan.Zero;
    }

    public TestClock(DateTimeOffset start) : base(start)
    {
    }
}
