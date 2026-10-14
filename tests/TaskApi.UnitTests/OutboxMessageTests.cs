using TaskApi.Messaging;

namespace TaskApi.UnitTests;

public sealed class OutboxMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_IsDueImmediately()
    {
        var message = OutboxMessage.Create("task.completed", "{}", traceParent: null, Now);

        Assert.Equal(Now, message.NextAttemptAt);
        Assert.Equal(0, message.Attempts);
    }

    [Fact]
    public void ScheduleRetry_BacksOffExponentiallyUpToLimit()
    {
        var message = OutboxMessage.Create("task.completed", "{}", traceParent: null, Now);
        var maxDelay = TimeSpan.FromSeconds(10);

        var delays = Enumerable.Range(0, 6)
            .Select(_ =>
            {
                message.ScheduleRetry("broker is down", Now, maxDelay);
                return (message.NextAttemptAt - Now).TotalSeconds;
            })
            .ToArray();

        Assert.Equal([1, 2, 4, 8, 10, 10], delays);
        Assert.Equal(6, message.Attempts);
        Assert.Equal("broker is down", message.LastError);
    }

    [Fact]
    public void ScheduleRetry_TruncatesLongError()
    {
        var message = OutboxMessage.Create("task.completed", "{}", traceParent: null, Now);

        message.ScheduleRetry(new string('x', OutboxMessage.MaxErrorLength + 1), Now, TimeSpan.FromMinutes(1));

        Assert.Equal(OutboxMessage.MaxErrorLength, message.LastError!.Length);
    }
}
