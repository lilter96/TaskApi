using TaskApi.Persistence;

namespace TaskApi.UnitTests;

public sealed class MicrosecondTimeProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 7, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1_234_567, 1_234_560)]
    [InlineData(1_234_560, 1_234_560)]
    [InlineData(9, 0)]
    public void GetUtcNow_DropsSubMicrosecondTicks(long ticks, long expected)
    {
        var clock = new MicrosecondTimeProvider(new FixedTimeProvider(Now.AddTicks(ticks)));

        Assert.Equal(Now.AddTicks(expected), clock.GetUtcNow());
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
