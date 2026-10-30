using System.Net;
using AssignHub.Domain.Enums;

namespace AssignHub.IntegrationTests.Http;

// SPEC §11.2 test 3.
public sealed class IdempotencyTests(SqlServerFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TwentyConcurrentPostsWithSameKey_CreateOneRequest()
    {
        for (var iteration = 0; iteration < 5; iteration++)
        {
            var connectionString = fixture.NewDatabaseConnectionString();
            var time = ApiHost.NewClock();
            await using var first = ApiHost.Start(connectionString, time);
            await using var second = ApiHost.Start(connectionString, time);

            var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
                (i % 2 == 0 ? first : second).Client.CreateRequestAsync("same-key", Subject.Legal, Priority.Urgent, Ct)));

            Assert.Single(responses, r => r.Status == HttpStatusCode.Created);
            Assert.Equal(19, responses.Count(r => r.Status == HttpStatusCode.OK));
            Assert.Single(responses.Select(r => r.Data.Id).Distinct());
            Assert.Equal(1, await new DbInspector(connectionString).QueryAsync(db => Task.FromResult(db.Requests.Count())));
        }
    }

    [Fact]
    public async Task SameKeyWithDifferentPayload_Returns409()
    {
        await using var api = ApiHost.Start(fixture.NewDatabaseConnectionString(), ApiHost.NewClock());
        await api.Client.CreateRequestAsync("abc", Subject.Legal, Priority.Normal, Ct);

        var conflict = await api.Client.CreateRequestAsync("abc", Subject.Legal, Priority.Normal, Ct, title: "Changed");

        Assert.Equal(HttpStatusCode.Conflict, conflict.Status);
        Assert.False(conflict.Body.IsSuccess);
        Assert.Null(conflict.Body.Data);
        Assert.Equal(["RequestKey 'abc' was already used with a different payload."], conflict.Body.Errors);
    }
}
