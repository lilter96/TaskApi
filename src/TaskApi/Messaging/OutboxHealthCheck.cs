using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TaskApi.Persistence;

namespace TaskApi.Messaging;

internal sealed class OutboxHealthCheck(
    TaskDbContext db,
    IOptions<OutboxOptions> options,
    TimeProvider timeProvider) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var oldest = await db.OutboxMessages.MinAsync(m => (DateTimeOffset?)m.CreatedAt, cancellationToken);
        if (oldest is null)
        {
            return HealthCheckResult.Healthy();
        }

        var lag = timeProvider.GetUtcNow() - oldest.Value;

        return lag > options.Value.DegradedAfter
            ? HealthCheckResult.Degraded($"The oldest undelivered message has been waiting for {lag:c}.")
            : HealthCheckResult.Healthy();
    }
}
