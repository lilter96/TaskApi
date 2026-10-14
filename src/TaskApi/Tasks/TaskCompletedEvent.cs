using TaskApi.Common;

namespace TaskApi.Tasks;

public sealed record TaskCompletedEvent(Guid TaskId, string Title, DateTimeOffset CompletedAt, Priority Priority)
    : IDomainEvent;
