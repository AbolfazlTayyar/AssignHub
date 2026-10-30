using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssignHub.Api.Contracts;
using AssignHub.Application.Experts;
using AssignHub.Application.Requests;
using AssignHub.Domain.Enums;

namespace AssignHub.IntegrationTests.Http;

internal sealed record ApiResult<T>(HttpStatusCode Status, ApiResponse<T> Body)
{
    public T Data => Body.Data ?? throw new InvalidOperationException($"No data; status {Status}, errors: {string.Join("; ", Body.Errors)}");
}

internal sealed class ApiClient(HttpClient http) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public HttpClient Http => http;

    public Task<ApiResult<RequestDto>> CreateRequestAsync(
        string key,
        Subject subject,
        Priority priority,
        CancellationToken cancellationToken,
        string title = "Title") =>
        SendAsync<RequestDto>(
            HttpMethod.Post,
            "/api/requests",
            new { requestKey = key, title, description = "Body", subject = subject.ToString(), priority = priority.ToString() },
            cancellationToken);

    public Task<ApiResult<RequestDto>> GetRequestAsync(int id, CancellationToken cancellationToken) =>
        SendAsync<RequestDto>(HttpMethod.Get, $"/api/requests/{id}", null, cancellationToken);

    public Task<ApiResult<RequestDto>> CloseRequestAsync(int id, CancellationToken cancellationToken) =>
        SendAsync<RequestDto>(HttpMethod.Post, $"/api/requests/{id}/close", null, cancellationToken);

    public Task<ApiResult<ExpertDto>> CreateExpertAsync(
        int capacity,
        Subject[] specialties,
        CancellationToken cancellationToken,
        bool isActive = true) =>
        SendAsync<ExpertDto>(
            HttpMethod.Post,
            "/api/experts",
            new { name = "Expert", capacity, specialties = specialties.Select(s => s.ToString()), isActive },
            cancellationToken);

    public Task<ApiResult<ExpertDto>> GetExpertAsync(int id, CancellationToken cancellationToken) =>
        SendAsync<ExpertDto>(HttpMethod.Get, $"/api/experts/{id}", null, cancellationToken);

    public Task<ApiResult<IReadOnlyList<ExpertDto>>> ListExpertsAsync(CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<ExpertDto>>(HttpMethod.Get, "/api/experts", null, cancellationToken);

    public Task<ApiResult<ExpertDto>> ActivateExpertAsync(int id, CancellationToken cancellationToken) =>
        SendAsync<ExpertDto>(HttpMethod.Post, $"/api/experts/{id}/activate", null, cancellationToken);

    public Task<ApiResult<ExpertDto>> DeactivateExpertAsync(int id, CancellationToken cancellationToken) =>
        SendAsync<ExpertDto>(HttpMethod.Post, $"/api/experts/{id}/deactivate", null, cancellationToken);

    public async Task<ApiResult<T>> SendAsync<T>(
        HttpMethod method,
        string url,
        object? body,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            message.Content = JsonContent.Create(body, options: Json);
        }

        using var response = await http.SendAsync(message, cancellationToken);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Json, cancellationToken)
            ?? throw new InvalidOperationException($"Empty body for {method} {url} ({response.StatusCode}).");
        return new ApiResult<T>(response.StatusCode, envelope);
    }

    public void Dispose() => http.Dispose();
}
