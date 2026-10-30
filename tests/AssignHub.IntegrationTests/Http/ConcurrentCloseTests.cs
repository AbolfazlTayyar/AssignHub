using System.Net;
using AssignHub.Domain.Enums;

namespace AssignHub.IntegrationTests.Http;

// SPEC §11.2 test 8.
public sealed class ConcurrentCloseTests(SqlServerFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InterleavedClosesAndCreates_KeepOpenCountEqualToAssignedCount()
    {
        for (var iteration = 0; iteration < 5; iteration++)
        {
            var connectionString = fixture.NewDatabaseConnectionString();
            var time = ApiHost.NewClock();
            await using var first = ApiHost.Start(connectionString, time);
            await using var second = ApiHost.Start(connectionString, time);
            ApiClient Pick(int i) => (i % 2 == 0 ? first : second).Client;

            await first.Client.CreateExpertAsync(3, [Subject.Legal], Ct);
            await first.Client.CreateExpertAsync(4, [Subject.Legal, Subject.Financial], Ct);
            await first.Client.CreateExpertAsync(2, [Subject.Financial], Ct);

            var open = new List<int>();
            for (var i = 0; i < 9; i++)
            {
                var subject = i % 3 == 0 ? Subject.Financial : Subject.Legal;
                open.Add((await first.Client.CreateRequestAsync($"open-{i}", subject, Priority.Normal, Ct)).Data.Id);
            }

            // Every open request is closed twice (a retry) while 20 new requests arrive on both instances.
            var closes = open.Concat(open).Select((id, i) => (Task)Pick(i).CloseRequestAsync(id, Ct));
            var creates = Enumerable.Range(0, 20).Select(i => (Task)Pick(i).CreateRequestAsync(
                $"new-{i}", i % 2 == 0 ? Subject.Legal : Subject.Financial, i % 3 == 0 ? Priority.Urgent : Priority.Normal, Ct));
            await Task.WhenAll(closes.Concat(creates));

            foreach (var id in open)
            {
                var request = await first.Client.GetRequestAsync(id, Ct);
                Assert.Equal(HttpStatusCode.OK, request.Status);
                Assert.Equal(RequestStatus.Closed, request.Data.Status);
            }

            await new DbInspector(connectionString).AssertCountersConsistentAsync(Ct);
        }
    }
}
