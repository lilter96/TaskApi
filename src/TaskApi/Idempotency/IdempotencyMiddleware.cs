using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Npgsql;
using TaskApi.Persistence;

namespace TaskApi.Idempotency;

internal sealed partial class IdempotencyMiddleware
{
    public const string KeyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    private readonly RequestDelegate _next;
    private readonly IdempotencyOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<IdempotencyMiddleware> _logger;

    public IdempotencyMiddleware(
        RequestDelegate next,
        IOptions<IdempotencyOptions> options,
        TimeProvider timeProvider,
        ILogger<IdempotencyMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IdempotentAttribute>() is null
            || !context.Request.Headers.TryGetValue(KeyHeader, out var header))
        {
            await _next(context);
            return;
        }

        if (!TryParseKey(header, out var key))
        {
            await Problem(
                    StatusCodes.Status400BadRequest,
                    $"{KeyHeader} must be a single value of 1 to {IdempotencyRecord.MaxKeyLength} characters.")
                .ExecuteAsync(context);
            return;
        }

        var db = context.RequestServices.GetRequiredService<TaskDbContext>();
        var requestHash = await ComputeRequestHashAsync(context.Request, context.RequestAborted);

        await using var transaction = await db.Database.BeginTransactionAsync(context.RequestAborted);

        bool acquired;
        try
        {
            acquired = await TryAcquireAsync(db, key, requestHash, context.RequestAborted);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await RespondInProgressAsync(context, key);
            return;
        }

        if (acquired)
        {
            await ExecuteAndStoreAsync(context, db, transaction, key);
            return;
        }

        var record = await db.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Key == key, context.RequestAborted);
        await transaction.RollbackAsync(CancellationToken.None);

        await ReplayAsync(context, key, requestHash, record);
    }

    private async Task<bool> TryAcquireAsync(
        TaskDbContext db,
        string key,
        byte[] requestHash,
        CancellationToken cancellationToken)
    {
        var lockTimeout = $"{(long)_options.LockTimeout.TotalMilliseconds}ms";
        var createdAt = _timeProvider.GetUtcNow();
        var expiresAt = createdAt + _options.KeyLifetime;

        await db.Database.ExecuteSqlAsync(
            $"SELECT set_config('lock_timeout', {lockTimeout}, true)",
            cancellationToken);

        var inserted = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO idempotency_records (key, request_hash, created_at, expires_at)
             VALUES ({key}, {requestHash}, {createdAt}, {expiresAt})
             ON CONFLICT (key) DO NOTHING
             """,
            cancellationToken);

        return inserted == 1;
    }

    private async Task ExecuteAndStoreAsync(
        HttpContext context,
        TaskDbContext db,
        IDbContextTransaction transaction,
        string key)
    {
        var response = context.Response;
        var responseBody = response.Body;
        using var buffer = new MemoryStream();
        response.Body = buffer;

        try
        {
            await _next(context);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            response.Body = responseBody;
        }

        if (response.StatusCode is >= 200 and < 300)
        {
            var statusCode = response.StatusCode;
            var contentType = response.ContentType;
            string? location = response.Headers.Location;
            var body = buffer.ToArray();

            await db.IdempotencyRecords
                .Where(r => r.Key == key)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(r => r.StatusCode, statusCode)
                        .SetProperty(r => r.ContentType, contentType)
                        .SetProperty(r => r.Location, location)
                        .SetProperty(r => r.ResponseBody, body),
                    CancellationToken.None);

            await transaction.CommitAsync(CancellationToken.None);
        }
        else
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(responseBody, context.RequestAborted);
    }

    private async Task ReplayAsync(HttpContext context, string key, byte[] requestHash, IdempotencyRecord? record)
    {
        if (record?.StatusCode is not { } statusCode)
        {
            await RespondInProgressAsync(context, key);
            return;
        }

        if (!record.RequestHash.AsSpan().SequenceEqual(requestHash))
        {
            LogKeyReused(key);
            await Problem(
                    StatusCodes.Status422UnprocessableEntity,
                    $"{KeyHeader} has already been used for a different request.")
                .ExecuteAsync(context);
            return;
        }

        LogReplayed(key, statusCode);

        var response = context.Response;
        response.StatusCode = statusCode;
        response.Headers[ReplayedHeader] = "true";

        if (record.ContentType is not null)
        {
            response.ContentType = record.ContentType;
        }

        if (record.Location is not null)
        {
            response.Headers.Location = record.Location;
        }

        if (record.ResponseBody is { Length: > 0 } body)
        {
            await response.Body.WriteAsync(body, context.RequestAborted);
        }
    }

    private async Task RespondInProgressAsync(HttpContext context, string key)
    {
        LogKeyInProgress(key);
        context.Response.Headers.RetryAfter = "1";
        await Problem(
                StatusCodes.Status409Conflict,
                $"A request with the same {KeyHeader} is still being processed.")
            .ExecuteAsync(context);
    }

    private static bool TryParseKey(StringValues header, [NotNullWhen(true)] out string? key)
    {
        key = header.Count == 1 ? header[0] : null;
        return !string.IsNullOrWhiteSpace(key) && key.Length <= IdempotencyRecord.MaxKeyLength;
    }

    private static async Task<byte[]> ComputeRequestHashAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        var bodyHash = await SHA256.HashDataAsync(request.Body, cancellationToken);
        request.Body.Position = 0;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{request.Method} {request.Path}{request.QueryString}\n"));
        hash.AppendData(bodyHash);

        return hash.GetHashAndReset();
    }

    private static ProblemHttpResult Problem(int statusCode, string detail) =>
        TypedResults.Problem(detail, statusCode: statusCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Replayed {StatusCode} response for idempotency key {Key}")]
    private partial void LogReplayed(string key, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency key {Key} was reused for a different request")]
    private partial void LogKeyReused(string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Idempotency key {Key} is held by a request in progress")]
    private partial void LogKeyInProgress(string key);
}
