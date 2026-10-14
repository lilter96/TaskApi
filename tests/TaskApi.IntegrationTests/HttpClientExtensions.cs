using System.Net;
using System.Net.Http.Json;
using TaskApi.Tasks;

namespace TaskApi.IntegrationTests;

internal static class HttpClientExtensions
{
    public static async Task<TaskResponse> CreateTaskAsync(
        this HttpClient client,
        string title,
        string? priority,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync("/tasks", new { title, priority }, cancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var task = await response.Content.ReadFromJsonAsync<TaskResponse>(cancellationToken);
        Assert.NotNull(task);

        return task;
    }

    public static Task<HttpResponseMessage> CompleteTaskAsync(
        this HttpClient client,
        Guid id,
        CancellationToken cancellationToken) =>
        client.PutAsync($"/tasks/{id}/complete", content: null, cancellationToken);
}
