using Microsoft.AspNetCore.Mvc;

namespace TaskApi.Tasks;

public sealed record CreateTaskRequest(string? Title, Priority? Priority);

public sealed record TaskListQuery(
    [FromQuery(Name = "isCompleted")] bool? IsCompleted,
    [FromQuery(Name = "priority")] string? Priority,
    [FromQuery(Name = "sort")] string? Sort,
    [FromQuery(Name = "offset")] int? Offset,
    [FromQuery(Name = "limit")] int? Limit)
{
    public const int MaxLimit = 1000;

    public Priority? ParsePriority() =>
        Enum.TryParse(Priority, ignoreCase: true, out Priority parsed) && Enum.IsDefined(parsed) ? parsed : null;

    public TaskSort? ParseSort() => TaskSort.TryParse(Sort, out var sort) ? sort : null;
}

public sealed record TaskResponse(
    Guid Id,
    string Title,
    bool IsCompleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    Priority Priority)
{
    public static TaskResponse From(TaskItem task) =>
        new(task.Id, task.Title, task.IsCompleted, task.CreatedAt, task.CompletedAt, task.Priority);
}
