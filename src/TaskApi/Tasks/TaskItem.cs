using TaskApi.Common;

namespace TaskApi.Tasks;

public sealed class TaskItem : AggregateRoot
{
    public const int TitleMaxLength = 200;

    private TaskItem()
    {
    }

    public Guid Id { get; private set; }

    public string Title { get; private set; } = null!;

    public bool IsCompleted { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public Priority Priority { get; private set; }

    public uint RowVersion { get; private set; }

    public static TaskItem Create(string title, Priority priority, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new TaskItem
        {
            Id = Guid.CreateVersion7(createdAt),
            Title = title.Trim(),
            Priority = priority,
            CreatedAt = createdAt,
        };
    }

    public void Complete(DateTimeOffset completedAt)
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException($"Task {Id} is already completed.");
        }

        IsCompleted = true;
        CompletedAt = completedAt;

        Raise(new TaskCompletedEvent(Id, Title, completedAt, Priority));
    }
}
