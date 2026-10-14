using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using TaskApi.Persistence;
using TaskApi.Tasks;

namespace TaskApi.IntegrationTests;

[Collection(nameof(TaskApiCollection))]
public sealed class CompleteTaskTests(TaskApiFactory factory)
{
    private const string UnreachableBroker = "amqp://guest:guest@127.0.0.1:1/";

    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Complete_PublishesTaskCompletedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var consumer = await RabbitMqTestConsumer.StartAsync(
            factory.RabbitMqConnectionString, "task.events", "task.completed", ct);

        var task = await _client.CreateTaskAsync("Buy milk", "High", ct);
        Assert.False(task.IsCompleted);
        Assert.Null(task.CompletedAt);

        var response = await _client.CompleteTaskAsync(task.Id, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var completed = await response.Content.ReadFromJsonAsync<TaskResponse>(ct);
        Assert.NotNull(completed);
        Assert.True(completed.IsCompleted);
        Assert.NotNull(completed.CompletedAt);

        var message = await consumer.WaitForAsync(IsAbout(task.Id), MessageTimeout, ct);

        Assert.Equal("task.events", message.Exchange);
        Assert.Equal("task.completed", message.RoutingKey);
        Assert.Equal("application/json", message.ContentType);
        Assert.True(Guid.TryParse(message.MessageId, out _));
        Assert.Equal("Buy milk", message.Body.GetProperty("title").GetString());
        Assert.Equal("High", message.Body.GetProperty("priority").GetString());
        Assert.Equal(completed.CompletedAt, message.Body.GetProperty("completedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Complete_WhenAlreadyCompleted_ReturnsConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var task = await _client.CreateTaskAsync("Pay rent", null, ct);
        await _client.CompleteTaskAsync(task.Id, ct);

        var response = await _client.CompleteTaskAsync(task.Id, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Complete_WhenCalledConcurrently_SucceedsAndPublishesOnlyOnce()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var consumer = await RabbitMqTestConsumer.StartAsync(
            factory.RabbitMqConnectionString, "task.events", "task.completed", ct);
        var task = await _client.CreateTaskAsync("Race", null, ct);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => _client.CompleteTaskAsync(task.Id, ct)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(responses.Length - 1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        await consumer.WaitForAsync(IsAbout(task.Id), MessageTimeout, ct);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => consumer.WaitForAsync(IsAbout(task.Id), TimeSpan.FromSeconds(2), ct));
    }

    [Fact]
    public async Task Complete_WhenRowChangedAfterRead_FailsOnRowVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        var created = await _client.CreateTaskAsync("Stale read", null, ct);

        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var first = firstScope.ServiceProvider.GetRequiredService<TaskDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<TaskDbContext>();

        var firstCopy = await first.Tasks.SingleAsync(t => t.Id == created.Id, ct);
        var secondCopy = await second.Tasks.SingleAsync(t => t.Id == created.Id, ct);

        firstCopy.Complete(DateTimeOffset.UtcNow);
        await first.SaveChangesAsync(ct);

        secondCopy.Complete(DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Complete_WhenRabbitMqIsUnavailable_CompletesTaskAndKeepsEventForRetry()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var offline = CreateFactory(factory.NewDatabaseConnectionString(), UnreachableBroker);
        var logs = offline.Services.GetFakeLogCollector();
        using var client = offline.CreateClient();

        var task = await client.CreateTaskAsync("Offline", "High", ct);
        var response = await client.CompleteTaskAsync(task.Id, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await client.GetFromJsonAsync<TaskResponse>($"/tasks/{task.Id}", ct);
        Assert.True(stored?.IsCompleted);

        await Eventually.AssertAsync(
            () => logs.GetSnapshot().Any(r => r.Level == LogLevel.Error && r.Message.Contains(task.Id.ToString())),
            MessageTimeout,
            ct);

        await using var scope = offline.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
        var pending = Assert.Single(await db.OutboxMessages.AsNoTracking().ToListAsync(ct));
        Assert.Contains(task.Id.ToString(), pending.Payload);
        Assert.True(pending.Attempts > 0);
        Assert.NotNull(pending.LastError);
    }

    [Fact]
    public async Task Complete_WhenRabbitMqComesBack_DeliversPendingEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = factory.NewDatabaseConnectionString();
        await using var consumer = await RabbitMqTestConsumer.StartAsync(
            factory.RabbitMqConnectionString, "task.events", "task.completed", ct);

        TaskResponse task;
        await using (var offline = CreateFactory(database, UnreachableBroker))
        {
            var logs = offline.Services.GetFakeLogCollector();
            using var client = offline.CreateClient();

            task = await client.CreateTaskAsync("Delivered later", null, ct);
            await client.CompleteTaskAsync(task.Id, ct);

            await Eventually.AssertAsync(
                () => logs.GetSnapshot().Any(r => r.Level == LogLevel.Error && r.Message.Contains(task.Id.ToString())),
                MessageTimeout,
                ct);
        }

        await using var online = CreateFactory(database, factory.RabbitMqConnectionString);
        _ = online.Services;

        var message = await consumer.WaitForAsync(IsAbout(task.Id), MessageTimeout, ct);
        Assert.Equal("Delivered later", message.Body.GetProperty("title").GetString());
    }

    private WebApplicationFactory<Program> CreateFactory(string database, string rabbitMqUri) =>
        factory.WithWebHostBuilder(builder => builder
            .UseSetting("ConnectionStrings:Tasks", database)
            .UseSetting("RabbitMq:Uri", rabbitMqUri)
            .ConfigureLogging(logging => logging.AddFakeLogging()));

    private static Func<ReceivedMessage, bool> IsAbout(Guid taskId) =>
        m => m.Body.GetProperty("taskId").GetGuid() == taskId;
}
