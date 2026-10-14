namespace TaskApi.Idempotency;

public sealed class IdempotencyRecord
{
    public const int MaxKeyLength = 255;

    private IdempotencyRecord()
    {
    }

    public string Key { get; private set; } = null!;

    public byte[] RequestHash { get; private set; } = null!;

    public int? StatusCode { get; private set; }

    public string? ContentType { get; private set; }

    public string? Location { get; private set; }

    public byte[]? ResponseBody { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }
}
