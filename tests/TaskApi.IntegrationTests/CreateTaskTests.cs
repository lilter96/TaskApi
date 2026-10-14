using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Http;
using TaskApi.Tasks;

namespace TaskApi.IntegrationTests;

[Collection(nameof(TaskApiCollection))]
public sealed class CreateTaskTests(TaskApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    public static TheoryData<string?> InvalidTitles =>
    [
        null,
        "",
        "   ",
        new string('a', TaskItem.TitleMaxLength + 1),
    ];

    [Fact]
    public async Task Create_WithoutPriority_CreatesOpenMediumTask()
    {
        var ct = TestContext.Current.CancellationToken;

        var task = await _client.CreateTaskAsync("  Buy milk  ", null, ct);

        Assert.Equal("Buy milk", task.Title);
        Assert.Equal(Priority.Medium, task.Priority);
        Assert.False(task.IsCompleted);
        Assert.Null(task.CompletedAt);
    }

    [Theory]
    [MemberData(nameof(InvalidTitles))]
    public async Task Create_WithInvalidTitle_ReturnsBadRequest(string? title)
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.PostAsJsonAsync("/tasks", new { title }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(ct);
        Assert.NotNull(problem);
        Assert.Contains("title", problem.Errors.Keys);
    }

    [Theory]
    [InlineData("\"Urgent\"")]
    [InlineData("42")]
    public async Task Create_WithUnknownPriority_ReturnsBadRequest(string priority)
    {
        var ct = TestContext.Current.CancellationToken;
        using var content = new StringContent(
            $$"""{ "title": "Buy milk", "priority": {{priority}} }""", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/tasks", content, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
