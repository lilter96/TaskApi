using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskApi.Persistence;

namespace TaskApi.Messaging;

internal sealed partial class OutboxDispatcher : BackgroundService
{
    public const string ActivitySourceName = "TaskApi.Outbox";

    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqPublisher _publisher;
    private readonly OutboxSignal _signal;
    private readonly OutboxOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxDispatcher> _logger;
    private readonly CancellationTokenSource _draining = new();

    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        RabbitMqPublisher publisher,
        OutboxSignal signal,
        IOptions<OutboxOptions> options,
        TimeProvider timeProvider,
        ILogger<OutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _signal = signal;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _draining.CancelAsync();

        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(_options.ShutdownTimeout);
            try
            {
                await (ExecuteTask ?? Task.CompletedTask).WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                LogDrainTimedOut(_options.ShutdownTimeout);
            }
        }

        await base.StopAsync(cancellationToken);
        await _publisher.CloseAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _draining.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            var moreDue = await TryDispatchBatchAsync(stoppingToken);
            if (moreDue)
            {
                continue;
            }

            if (_draining.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await _signal.WaitAsync(_options.PollInterval, _draining.Token);
            }
            catch (OperationCanceledException) when (_draining.IsCancellationRequested)
            {
            }
        }
    }

    private async Task<bool> TryDispatchBatchAsync(CancellationToken stoppingToken)
    {
        try
        {
            return await DispatchBatchAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            LogDispatchFailed(ex);
            return false;
        }
    }

    private async Task<bool> DispatchBatchAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);

        var now = _timeProvider.GetUtcNow();

        var messages = await db.OutboxMessages
            .FromSql(
                $"""
                 SELECT * FROM outbox_messages
                 WHERE next_attempt_at <= {now}
                 ORDER BY next_attempt_at, id
                 LIMIT {_options.BatchSize}
                 FOR UPDATE SKIP LOCKED
                 """)
            .ToListAsync(stoppingToken);

        var delivered = 0;
        Exception? failure = null;

        foreach (var message in messages)
        {
            if (failure is null)
            {
                try
                {
                    await PublishAsync(message, stoppingToken);
                    db.OutboxMessages.Remove(message);
                    delivered++;
                    continue;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            }

            message.ScheduleRetry(failure.Message, now, _options.MaxRetryDelay);
            LogPublishFailed(failure, message.RoutingKey, message.Id, message.Attempts, message.NextAttemptAt, message.Payload);
        }

        // Not cancellable: messages that reached the broker must be removed even when shutdown is being forced.
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);

        if (delivered > 0)
        {
            LogDelivered(delivered);
        }

        return failure is null && delivered == _options.BatchSize;
    }

    private async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("outbox publish", ActivityKind.Internal, message.TraceParent);
        activity?.SetTag("messaging.message.id", message.Id);

        await _publisher.PublishAsync(message, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Delivered {Count} outbox messages")]
    private partial void LogDelivered(int count);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to publish {RoutingKey} message {MessageId} (attempt {Attempt}), next attempt at {NextAttemptAt}: {Payload}")]
    private partial void LogPublishFailed(
        Exception exception,
        string routingKey,
        Guid messageId,
        int attempt,
        DateTimeOffset nextAttemptAt,
        string payload);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox dispatch failed")]
    private partial void LogDispatchFailed(Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Outbox was not drained within {Timeout}; remaining messages will be delivered after restart")]
    private partial void LogDrainTimedOut(TimeSpan timeout);
}
