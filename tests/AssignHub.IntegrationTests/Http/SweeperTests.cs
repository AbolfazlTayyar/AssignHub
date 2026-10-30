using AssignHub.Domain.Enums;

namespace AssignHub.IntegrationTests.Http;

// SPEC §11.2 test 9.
public sealed class SweeperTests(SqlServerFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MissedInlineDrain_IsRecoveredWithinOneSweepInterval()
    {
        var connectionString = fixture.NewDatabaseConnectionString();
        var time = ApiHost.NewClock();
        var interval = TimeSpan.FromSeconds(30);
        await using var api = ApiHost.Start(connectionString, time, interval);
        var expert = (await api.Client.CreateExpertAsync(2, [Subject.Financial], Ct)).Data;

        var requestId = await new DbInspector(connectionString)
            .InsertPendingAsync(Subject.Financial, Priority.Normal, time.GetUtcNow().UtcDateTime, Ct);
        Assert.Equal(RequestStatus.Pending, (await api.Client.GetRequestAsync(requestId, Ct)).Data.Status);

        time.Advance(interval);

        var request = await Eventually.WaitForAsync(
            async () => (await api.Client.GetRequestAsync(requestId, Ct)).Data,
            r => r.Status == RequestStatus.Assigned,
            Ct);
        Assert.Equal(RequestStatus.Assigned, request.Status);
        Assert.Equal(expert.Id, request.ExpertId);
        Assert.Equal(time.GetUtcNow().UtcDateTime, request.AssignedAt);
    }
}
