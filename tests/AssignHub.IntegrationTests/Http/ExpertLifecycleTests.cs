using System.Net;
using AssignHub.Domain.Enums;

namespace AssignHub.IntegrationTests.Http;

// SPEC §11.2 test 7.
public sealed class ExpertLifecycleTests(SqlServerFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deactivate_KeepsExistingRequestsAndStopsNewOnes()
    {
        await using var api = ApiHost.Start(fixture.NewDatabaseConnectionString(), ApiHost.NewClock());
        var expert = (await api.Client.CreateExpertAsync(5, [Subject.Legal], Ct)).Data;
        var existing = (await api.Client.CreateRequestAsync("existing", Subject.Legal, Priority.Normal, Ct)).Data;

        var deactivated = await api.Client.DeactivateExpertAsync(expert.Id, Ct);
        var fresh = (await api.Client.CreateRequestAsync("fresh", Subject.Legal, Priority.Urgent, Ct)).Data;

        Assert.Equal(HttpStatusCode.OK, deactivated.Status);
        Assert.False(deactivated.Data.IsActive);
        Assert.Equal(1, deactivated.Data.OpenCount);
        Assert.Equal(RequestStatus.Pending, fresh.Status);
        var kept = (await api.Client.GetRequestAsync(existing.Id, Ct)).Data;
        Assert.Equal(RequestStatus.Assigned, kept.Status);
        Assert.Equal(expert.Id, kept.ExpertId);

        var closed = await api.Client.CloseRequestAsync(existing.Id, Ct);

        Assert.Equal(HttpStatusCode.OK, closed.Status);
        Assert.Equal(RequestStatus.Closed, closed.Data.Status);
        Assert.Equal(RequestStatus.Pending, (await api.Client.GetRequestAsync(fresh.Id, Ct)).Data.Status);
        Assert.Equal(0, (await api.Client.GetExpertAsync(expert.Id, Ct)).Data.OpenCount);
    }

    [Fact]
    public async Task Activate_DrainsTheQueue()
    {
        await using var api = ApiHost.Start(fixture.NewDatabaseConnectionString(), ApiHost.NewClock());
        var expert = (await api.Client.CreateExpertAsync(2, [Subject.Technical], Ct, isActive: false)).Data;
        var first = (await api.Client.CreateRequestAsync("t1", Subject.Technical, Priority.Normal, Ct)).Data;
        var second = (await api.Client.CreateRequestAsync("t2", Subject.Technical, Priority.Normal, Ct)).Data;
        var third = (await api.Client.CreateRequestAsync("t3", Subject.Technical, Priority.Normal, Ct)).Data;
        Assert.Equal(RequestStatus.Pending, first.Status);

        var activated = await api.Client.ActivateExpertAsync(expert.Id, Ct);

        Assert.Equal(HttpStatusCode.OK, activated.Status);
        Assert.True(activated.Data.IsActive);
        Assert.Equal(2, activated.Data.OpenCount);
        Assert.Equal(1.0, activated.Data.LoadRatio);
        Assert.Equal(RequestStatus.Assigned, (await api.Client.GetRequestAsync(first.Id, Ct)).Data.Status);
        Assert.Equal(RequestStatus.Assigned, (await api.Client.GetRequestAsync(second.Id, Ct)).Data.Status);
        Assert.Equal(RequestStatus.Pending, (await api.Client.GetRequestAsync(third.Id, Ct)).Data.Status);
    }
}
