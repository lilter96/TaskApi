using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskApi.Idempotency;
using TaskApi.Persistence;
using TaskApi.Tasks;

namespace TaskApi.IntegrationTests;

[Collection(nameof(TaskApiCollection))]
public sealed class IdempotencyTests(TaskApiFactory factory)
{
    private const string KeyHeader = "Idempotency-Key";
    private const string ReplayedHeader = "Idempotent-Replayed";

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Create_RetriedWithSameKey_ReplaysResponseWithoutDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = NewKey();
        var title = $"Idempotent {key}";

        var first = await _client.SendAsync(CreateTask(key, title), ct);
        var second = await _client.SendAsync(CreateTask(key, title), ct);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.False(first.Headers.Contains(ReplayedHeader));
        Assert.Equal("true", Assert.Single(second.Headers.GetValues(ReplayedHeader)));
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.Equal(await first.Content.ReadAsStringAsync(ct), await second.Content.ReadAsStringAsync(ct));

        var tasks = await _client.GetFromJsonAsync<List<TaskResponse>>("/tasks", ct);
        Assert.Single(tasks!, t => t.Title == title);
    }

    [Fact]
    public async Task Create_SameKeyWithDifferentBody_ReturnsUnprocessableEntity()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = NewKey();
        await _client.SendAsync(CreateTask(key, "Original"), ct);

        var response = await _client.SendAsync(CreateTask(key, "Changed"), ct);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Create_RejectedRequest_ReleasesKeyForCorrectedRetry()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = NewKey();

        var rejected = await _client.SendAsync(CreateTask(key, "   "), ct);
        var corrected = await _client.SendAsync(CreateTask(key, "Fixed title"), ct);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);
        Assert.False(corrected.Headers.Contains(ReplayedHeader));
    }

    [Fact]
    public async Task Create_ConcurrentRetriesWithSameKey_CreateSingleTask()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = NewKey();
        var title = $"Retry storm {key}";

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => _client.SendAsync(CreateTask(key, title), ct)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Single(responses, r => !r.Headers.Contains(ReplayedHeader));
        Assert.Single(responses.Select(r => r.Headers.Location).Distinct());

        var tasks = await _client.GetFromJsonAsync<List<TaskResponse>>("/tasks", ct);
        Assert.Single(tasks!, t => t.Title == title);
    }

    [Fact]
    public async Task Complete_RepeatedWithSameKey_StillReturnsConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var task = await _client.CreateTaskAsync("Complete twice", null, ct);
        var key = NewKey();

        var first = await _client.SendAsync(CompleteTask(task.Id, key), ct);
        var second = await _client.SendAsync(CompleteTask(task.Id, key), ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Request_WhileKeyIsHeldByAnotherRequest_ReturnsConflictWithRetryAfter()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var impatientFactory = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Idempotency:LockTimeout", "00:00:00.200"));
        using var client = impatientFactory.CreateClient();
        var key = NewKey();

        await using var scope = impatientFactory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
        await using var inFlight = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO idempotency_records (key, request_hash, created_at, expires_at)
             VALUES ({key}, {new byte[32]}, now(), now() + interval '1 hour')
             """,
            ct);

        var response = await client.SendAsync(CreateTask(key, "Blocked"), ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
    }

    public static TheoryData<string[]> InvalidKeys => new()
    {
        { ["   "] },
        { [new string('k', IdempotencyRecord.MaxKeyLength + 1)] },
        { ["first", "second"] },
    };

    [Theory]
    [MemberData(nameof(InvalidKeys))]
    public async Task Request_WithInvalidKey_ReturnsBadRequest(string[] keys)
    {
        var ct = TestContext.Current.CancellationToken;
        var request = CreateTask(key: null, "Invalid key");
        request.Headers.TryAddWithoutValidation(KeyHeader, keys);

        var response = await _client.SendAsync(request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string NewKey() => Guid.NewGuid().ToString();

    private static HttpRequestMessage CreateTask(string? key, string title) =>
        WithKey(new HttpRequestMessage(HttpMethod.Post, "/tasks") { Content = JsonContent.Create(new { title }) }, key);

    private static HttpRequestMessage CompleteTask(Guid id, string key) =>
        WithKey(new HttpRequestMessage(HttpMethod.Put, $"/tasks/{id}/complete"), key);

    private static HttpRequestMessage WithKey(HttpRequestMessage request, string? key)
    {
        if (key is not null)
        {
            request.Headers.Add(KeyHeader, key);
        }

        return request;
    }
}
