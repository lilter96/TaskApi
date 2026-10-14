using System.ComponentModel.DataAnnotations;

namespace TaskApi.Messaging;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    [Range(typeof(TimeSpan), "00:00:00.050", "00:01:00")]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    [Range(1, 1000)]
    public int BatchSize { get; set; } = 100;

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(5);

    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan DegradedAfter { get; set; } = TimeSpan.FromMinutes(1);
}
