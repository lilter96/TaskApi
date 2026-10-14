namespace TaskApi.Messaging;

public sealed class OutboxMessage
{
    public const int MaxErrorLength = 2000;

    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    public string RoutingKey { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public string? TraceParent { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage Create(string routingKey, string payload, string? traceParent, DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.CreateVersion7(createdAt),
            RoutingKey = routingKey,
            Payload = payload,
            TraceParent = traceParent,
            CreatedAt = createdAt,
            NextAttemptAt = createdAt,
        };

    public void ScheduleRetry(string error, DateTimeOffset now, TimeSpan maxDelay)
    {
        Attempts++;
        NextAttemptAt = now + TimeSpan.FromSeconds(Math.Min(Math.Pow(2, Attempts - 1), maxDelay.TotalSeconds));
        LastError = error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
    }
}
