using System.ComponentModel.DataAnnotations;

namespace TaskApi.Idempotency;

public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00")]
    public TimeSpan KeyLifetime { get; set; } = TimeSpan.FromHours(24);

    [Range(typeof(TimeSpan), "00:00:00.050", "00:01:00")]
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(5);

    [Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}
