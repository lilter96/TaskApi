using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskApi.Persistence;

namespace TaskApi.Idempotency;

internal sealed partial class IdempotencyCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IdempotencyOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IdempotencyCleanupService> _logger;

    public IdempotencyCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<IdempotencyOptions> options,
        TimeProvider timeProvider,
        ILogger<IdempotencyCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.CleanupInterval, _timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await DeleteExpiredAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCleanupFailed(ex);
            }
        }
    }

    private async Task DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
        var now = _timeProvider.GetUtcNow();

        var deleted = await db.IdempotencyRecords
            .Where(r => r.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
        {
            LogDeleted(deleted);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} expired idempotency records")]
    private partial void LogDeleted(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to delete expired idempotency records")]
    private partial void LogCleanupFailed(Exception exception);
}
