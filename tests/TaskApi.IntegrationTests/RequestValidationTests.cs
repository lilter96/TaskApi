using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using TaskApi.Tasks;

namespace TaskApi.IntegrationTests;

[Collection(nameof(TaskApiCollection))]
public sealed class RequestValidationTests(TaskApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("GET", "/tasks/not-a-guid")]
    [InlineData("PUT", "/tasks/not-a-guid/complete")]
    [InlineData("DELETE", "/tasks/not-a-guid")]
    public async Task Request_WithMalformedId_ReturnsBadRequest(string method, string path)
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Urgent")]
    [InlineData("42")]
    public async Task GetList_ByUnknownPriority_ReturnsBadRequest(string priority)
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync($"/tasks?priority={priority}", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(ct);
        Assert.Contains("priority", problem!.Errors.Keys);
    }

    [Theory]
    [InlineData("dueDate")]
    [InlineData("-")]
    [InlineData("--priority")]
    [InlineData("1")]
    public async Task GetList_ByUnknownSort_ReturnsBadRequest(string sort)
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await _client.GetAsync($"/tasks?sort={Uri.EscapeDataString(sort)}", ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(ct);
        Assert.Contains("sort", problem!.Errors.Keys);
    }
}
