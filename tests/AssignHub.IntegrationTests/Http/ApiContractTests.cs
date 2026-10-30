using System.Net;
using System.Net.Http.Json;
using AssignHub.Api.Contracts;
using AssignHub.Application.Abstractions;
using AssignHub.Domain.Enums;
using AssignHub.Domain.Experts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AssignHub.IntegrationTests.Http;

// SPEC §9.0: every response, including failures the controllers never see, uses the envelope.
public sealed class ApiContractTests(SqlServerFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Created_ReturnsEnvelopeAndLocation()
    {
        await using var api = ApiHost.Start(fixture.NewDatabaseConnectionString(), ApiHost.NewClock());

        using var response = await api.Client.Http.PostAsJsonAsync(
            "/api/experts", new { name = "Sara", capacity = 3, specialties = new[] { "Legal" } }, Ct);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<Dictionary<string, object>>>(Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(body!.IsSuccess);
        Assert.Empty(body.Errors);
        Assert.Equal($"/api/experts/{body.Data!["id"]}", response.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData("""{"requestKey":"k","title":"t","subject":"Medical","priority":"Urgent"}""", "The value of 'subject' is invalid.")]
    [InlineData("""{"requestKey":"k","title":"t","subject":2,"priority":"Urgent"}""", "The value of 'subject' is invalid.")]
    [InlineData("""{"title":"t","subject":"Legal","priority":"Urgent"}""", "The RequestKey field is required.")]
    [InlineData("""{"requestKey":"k","title":""", "The value of 'title' is invalid.")]
    [InlineData("not json", "The request body is not valid JSON.")]
    public async Task InvalidRequestShape_Returns400Envelope(string json, string expectedError)
    {
        await using var api = ApiHost.Start(fixture.NewDatabaseConnectionString(), ApiHost.NewClock());

        var result = await PostRawAsync(api, "/api/requests", json);

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        Assert.False(result.Body.IsSuccess);
        Assert.Equal([expectedError], result.Body.Errors);
    }

    [Fact]
    public async Task DomainRuleViolation_Returns400Envelope()
    {
        await using var api = ApiHost.Start(fixture.NewDatabaseConnectionString(), ApiHost.NewClock());

        var result = await api.Client.CreateExpertAsync(1001, [Subject.Legal], Ct);

        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
        Assert.Equal(["Capacity must be between 1 and 1000."], result.Body.Errors);
    }

    [Fact]
    public async Task NotFoundAndUnknownRoute_Return404Envelope()
    {
        await using var api = ApiHost.Start(fixture.NewDatabaseConnectionString(), ApiHost.NewClock());

        var missing = await api.Client.GetRequestAsync(12345, Ct);
        var route = await api.Client.SendAsync<object>(HttpMethod.Get, "/api/nothing-here", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
        Assert.Equal(["Request 12345 was not found."], missing.Body.Errors);
        Assert.Equal(HttpStatusCode.NotFound, route.Status);
        Assert.False(route.Body.IsSuccess);
    }

    [Fact]
    public async Task ClosingPendingRequest_Returns409Envelope()
    {
        await using var api = ApiHost.Start(fixture.NewDatabaseConnectionString(), ApiHost.NewClock());
        var pending = (await api.Client.CreateRequestAsync("p", Subject.Technical, Priority.Normal, Ct)).Data;

        var result = await api.Client.CloseRequestAsync(pending.Id, Ct);

        Assert.Equal(HttpStatusCode.Conflict, result.Status);
        Assert.False(result.Body.IsSuccess);
    }

    [Fact]
    public async Task UnhandledException_Returns500EnvelopeWithoutDetails()
    {
        await using var api = ApiHost.Start(
            fixture.NewDatabaseConnectionString(),
            ApiHost.NewClock(),
            configureServices: services =>
            {
                services.RemoveAll<IExpertStore>();
                services.AddScoped<IExpertStore, ThrowingExpertStore>();
            });

        var result = await api.Client.ListExpertsAsync(Ct);

        Assert.Equal(HttpStatusCode.InternalServerError, result.Status);
        Assert.False(result.Body.IsSuccess);
        Assert.Equal(["An unexpected error occurred."], result.Body.Errors);
    }

    private static async Task<ApiResult<object>> PostRawAsync(ApiHost api, string url, string json)
    {
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await api.Client.Http.PostAsync(url, content, Ct);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(Ct);
        return new ApiResult<object>(response.StatusCode, body!);
    }

    private sealed class ThrowingExpertStore : IExpertStore
    {
        private const string Secret = "connection details that must not leak";

        public Task AddAsync(Expert expert, CancellationToken cancellationToken) => throw new InvalidOperationException(Secret);

        public Task<Expert?> GetAsync(int id, CancellationToken cancellationToken) => throw new InvalidOperationException(Secret);

        public Task<Expert?> FindForUpdateAsync(int id, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Secret);

        public Task<IReadOnlyList<Expert>> ListAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Secret);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new InvalidOperationException(Secret);
    }
}
