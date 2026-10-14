namespace TaskApi.Persistence;

// timestamptz stores microseconds; a finer clock makes API responses and events differ from what is persisted.
public sealed class MicrosecondTimeProvider(TimeProvider inner) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public override long TimestampFrequency => inner.TimestampFrequency;

    public override DateTimeOffset GetUtcNow()
    {
        var now = inner.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    public override long GetTimestamp() => inner.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        inner.CreateTimer(callback, state, dueTime, period);
}
