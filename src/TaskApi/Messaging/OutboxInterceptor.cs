using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaskApi.Common;
using TaskApi.Tasks;

namespace TaskApi.Messaging;

internal sealed class OutboxInterceptor(TimeProvider timeProvider, OutboxSignal signal) : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly object Pending = new();

    private readonly ConditionalWeakTable<DbContext, object> _contextsWithMessages = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        EnqueueDomainEvents(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        EnqueueDomainEvents(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        NotifyDispatcher(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        NotifyDispatcher(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData.Context);

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    private void EnqueueDomainEvents(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var aggregates = context.ChangeTracker.Entries<AggregateRoot>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();

        if (aggregates.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var traceParent = Activity.Current?.Id;

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                context.Add(OutboxMessage.Create(
                    RoutingKeyOf(domainEvent),
                    JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), SerializerOptions),
                    traceParent,
                    now));
            }

            aggregate.ClearDomainEvents();
        }

        _contextsWithMessages.AddOrUpdate(context, Pending);
    }

    private void NotifyDispatcher(DbContext? context)
    {
        if (context is not null && _contextsWithMessages.Remove(context))
        {
            signal.Notify();
        }
    }

    private void Forget(DbContext? context)
    {
        if (context is not null)
        {
            _contextsWithMessages.Remove(context);
        }
    }

    private static string RoutingKeyOf(IDomainEvent domainEvent) => domainEvent switch
    {
        TaskCompletedEvent => "task.completed",
        _ => throw new NotSupportedException($"No routing key is defined for {domainEvent.GetType().Name}."),
    };
}
