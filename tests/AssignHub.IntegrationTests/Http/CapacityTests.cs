using System.Net;
using AssignHub.Application.Requests;
using AssignHub.Domain.Enums;

namespace AssignHub.IntegrationTests.Http;

// SPEC §11.2 tests 1 and 2: the task's required "20 concurrent requests vs capacity 5" scenario.
public sealed class CapacityTests(SqlServerFixture fixture)
{
    private const int Iterations = 10;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwentyConcurrentRequests_CapacityFive_AssignsExactlyFive_SingleInstance()
    {
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var connectionString = fixture.NewDatabaseConnectionString();
            await using var api = ApiHost.Start(connectionString, ApiHost.NewClock());
            var expert = (await api.Client.CreateExpertAsync(5, [Subject.Legal], Ct)).Data;

            var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
                api.Client.CreateRequestAsync($"req-{i}", Subject.Legal, Priority.Normal, Ct)));

            await AssertExactlyFiveAssignedAsync(responses, expert.Id, api.Client, connectionString);
        }
    }

    [Fact]
    public async Task TwentyConcurrentRequests_CapacityFive_AssignsExactlyFive_TwoInstances()
    {
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var connectionString = fixture.NewDatabaseConnectionString();
            var time = ApiHost.NewClock();

            // Both instances start together on a database that does not exist yet (SPEC §10).
            var hosts = await Task.WhenAll(
                Task.Run(() => ApiHost.Start(connectionString, time), Ct),
                Task.Run(() => ApiHost.Start(connectionString, time), Ct));
            await using var first = hosts[0];
            await using var second = hosts[1];

            var expert = (await first.Client.CreateExpertAsync(5, [Subject.Legal], Ct)).Data;

            var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
                (i % 2 == 0 ? first : second).Client.CreateRequestAsync($"req-{i}", Subject.Legal, Priority.Normal, Ct)));

            await AssertExactlyFiveAssignedAsync(responses, expert.Id, second.Client, connectionString);
        }
    }

    private static async Task AssertExactlyFiveAssignedAsync(
        ApiResult<RequestDto>[] responses,
        int expertId,
        ApiClient client,
        string connectionString)
    {
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.Status));
        Assert.Equal(20, responses.Select(r => r.Data.Id).Distinct().Count());

        // A response shows its request after its own drain; a later drain may still assign it,
        // so the exact split is checked on the final state, not on the responses.
        Assert.InRange(responses.Count(r => r.Data.Status == RequestStatus.Assigned), 0, 5);

        var final = await Task.WhenAll(responses.Select(r => client.GetRequestAsync(r.Data.Id, Ct)));
        var assigned = final.Select(r => r.Data).Where(r => r.Status == RequestStatus.Assigned).ToList();
        var pending = final.Select(r => r.Data).Where(r => r.Status == RequestStatus.Pending).ToList();

        Assert.Equal(5, assigned.Count);
        Assert.Equal(15, pending.Count);
        Assert.All(assigned, r => Assert.Equal(expertId, r.ExpertId));
        Assert.All(pending, r => Assert.Null(r.ExpertId));
        Assert.Equal(5, (await client.GetExpertAsync(expertId, Ct)).Data.OpenCount);

        var db = new DbInspector(connectionString);
        Assert.Equal(5, await db.CountAsync(RequestStatus.Assigned, Ct));
        Assert.Equal(15, await db.CountAsync(RequestStatus.Pending, Ct));
        await db.AssertCountersConsistentAsync(Ct);
    }
}
