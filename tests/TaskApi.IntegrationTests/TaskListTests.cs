using System.Net.Http.Json;
using TaskApi.Tasks;

namespace TaskApi.IntegrationTests;

[Collection(nameof(TaskApiCollection))]
public sealed class TaskListTests(TaskApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("high")]
    [InlineData("HIGH")]
    [InlineData("High")]
    public async Task GetList_ByPriority_IgnoresCase(string priority)
    {
        var ct = TestContext.Current.CancellationToken;
        var created = await _client.CreateTaskAsync($"Filter {Guid.NewGuid()}", "High", ct);

        var tasks = await _client.GetFromJsonAsync<List<TaskResponse>>($"/tasks?priority={priority}", ct);

        Assert.NotNull(tasks);
        Assert.Contains(tasks, t => t.Id == created.Id);
        Assert.All(tasks, t => Assert.Equal(Priority.High, t.Priority));
    }

    [Theory]
    [InlineData("-priority", new[] { Priority.High, Priority.Medium, Priority.Low })]
    [InlineData("priority", new[] { Priority.Low, Priority.Medium, Priority.High })]
    public async Task GetList_SortedByPriority_UsesSeverityOrder(string sort, Priority[] expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var marker = Guid.NewGuid().ToString();
        foreach (var priority in new[] { "Medium", "Low", "High" })
        {
            await _client.CreateTaskAsync($"{marker} {priority}", priority, ct);
        }

        var tasks = await _client.GetFromJsonAsync<List<TaskResponse>>($"/tasks?sort={sort}", ct);

        Assert.Equal(expected, tasks!.Where(t => t.Title.StartsWith(marker)).Select(t => t.Priority));
    }

    [Fact]
    public async Task GetList_WithoutSort_ReturnsNewestFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        var older = await _client.CreateTaskAsync("Older", null, ct);
        var newer = await _client.CreateTaskAsync("Newer", null, ct);

        var tasks = await _client.GetFromJsonAsync<List<TaskResponse>>("/tasks?limit=2", ct);

        Assert.Equal([newer.Id, older.Id], tasks!.Select(t => t.Id));
    }
}
