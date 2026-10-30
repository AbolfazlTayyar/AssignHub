using System.Net;
using AssignHub.Domain.Enums;

namespace AssignHub.IntegrationTests.Http;

// SPEC §11.2 tests 4, 5 and 6.
public sealed class QueueTests(SqlServerFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Close_FreesSlotAndAssignsHeadOfQueue()
    {
        await using var api = ApiHost.Start(fixture.NewDatabaseConnectionString(), ApiHost.NewClock());
        var expert = (await api.Client.CreateExpertAsync(1, [Subject.Legal], Ct)).Data;
        var busy = (await api.Client.CreateRequestAsync("busy", Subject.Legal, Priority.Normal, Ct)).Data;
        var waiting = (await api.Client.CreateRequestAsync("waiting", Subject.Legal, Priority.Normal, Ct)).Data;
        Assert.Equal(RequestStatus.Assigned, busy.Status);
        Assert.Equal(RequestStatus.Pending, waiting.Status);

        var closed = await api.Client.CloseRequestAsync(busy.Id, Ct);

        Assert.Equal(HttpStatusCode.OK, closed.Status);
        Assert.Equal(RequestStatus.Closed, closed.Data.Status);
        var next = (await api.Client.GetRequestAsync(waiting.Id, Ct)).Data;
        Assert.Equal(RequestStatus.Assigned, next.Status);
        Assert.Equal(expert.Id, next.ExpertId);
        Assert.Equal(1, (await api.Client.GetExpertAsync(expert.Id, Ct)).Data.OpenCount);
    }

    [Fact]
    public async Task Close_AssignsUrgentBeforeOlderNormal()
    {
        var time = ApiHost.NewClock();
        await using var api = ApiHost.Start(fixture.NewDatabaseConnectionString(), time);
        await api.Client.CreateExpertAsync(1, [Subject.Financial], Ct);
        var busy = (await api.Client.CreateRequestAsync("busy", Subject.Financial, Priority.Normal, Ct)).Data;
        time.Advance(TimeSpan.FromMinutes(1));
        var normal = (await api.Client.CreateRequestAsync("normal", Subject.Financial, Priority.Normal, Ct)).Data;
        time.Advance(TimeSpan.FromMinutes(1));
        var urgent = (await api.Client.CreateRequestAsync("urgent", Subject.Financial, Priority.Urgent, Ct)).Data;

        await api.Client.CloseRequestAsync(busy.Id, Ct);

        Assert.Equal(RequestStatus.Assigned, (await api.Client.GetRequestAsync(urgent.Id, Ct)).Data.Status);
        Assert.Equal(RequestStatus.Pending, (await api.Client.GetRequestAsync(normal.Id, Ct)).Data.Status);
    }

    [Fact]
    public async Task Aging_WorkedExampleFromSpec()
    {
        // SPEC §7.3: T = 10 min; one Legal expert E with capacity 1, busy from the start.
        var time = ApiHost.NewClock();
        await using var api = ApiHost.Start(fixture.NewDatabaseConnectionString(), time);
        void At(int hour, int minute) => time.SetUtcNow(new DateTimeOffset(2026, 10, 30, hour, minute, 0, TimeSpan.Zero));
        async Task<RequestStatus> StatusOf(int id) => (await api.Client.GetRequestAsync(id, Ct)).Data.Status;

        At(9, 40);
        await api.Client.CreateExpertAsync(1, [Subject.Legal], Ct);
        var busy = (await api.Client.CreateRequestAsync("busy", Subject.Legal, Priority.Normal, Ct)).Data;

        At(9, 45);
        var n1 = (await api.Client.CreateRequestAsync("N1", Subject.Legal, Priority.Normal, Ct)).Data;
        At(9, 50);
        var u1 = (await api.Client.CreateRequestAsync("U1", Subject.Legal, Priority.Urgent, Ct)).Data;

        At(9, 52); // N1 has waited 7 min, not aged: U1 first
        await api.Client.CloseRequestAsync(busy.Id, Ct);
        Assert.Equal(RequestStatus.Assigned, await StatusOf(u1.Id));
        Assert.Equal(RequestStatus.Pending, await StatusOf(n1.Id));

        At(9, 58);
        var u2 = (await api.Client.CreateRequestAsync("U2", Subject.Legal, Priority.Urgent, Ct)).Data;

        At(10, 1); // N1 has waited 16 min: effectively urgent and older than U2
        await api.Client.CloseRequestAsync(u1.Id, Ct);
        Assert.Equal(RequestStatus.Assigned, await StatusOf(n1.Id));
        Assert.Equal(RequestStatus.Pending, await StatusOf(u2.Id));

        At(10, 5);
        await api.Client.CloseRequestAsync(n1.Id, Ct);
        Assert.Equal(RequestStatus.Assigned, await StatusOf(u2.Id));
    }

    [Fact]
    public async Task UnassignableHead_DoesNotBlockLegalRequestsBehindIt()
    {
        await using var api = ApiHost.Start(fixture.NewDatabaseConnectionString(), ApiHost.NewClock());
        await api.Client.CreateExpertAsync(5, [Subject.Legal], Ct);

        var technical = (await api.Client.CreateRequestAsync("tech", Subject.Technical, Priority.Urgent, Ct)).Data;
        var legal1 = (await api.Client.CreateRequestAsync("legal-1", Subject.Legal, Priority.Normal, Ct)).Data;
        var legal2 = (await api.Client.CreateRequestAsync("legal-2", Subject.Legal, Priority.Normal, Ct)).Data;

        Assert.Equal(RequestStatus.Pending, technical.Status);
        Assert.Equal(RequestStatus.Assigned, legal1.Status);
        Assert.Equal(RequestStatus.Assigned, legal2.Status);
    }

    [Fact]
    public async Task UnassignableHead_DoesNotBlockAFullQueueSweep()
    {
        // The sweep drains every subject at once, so the Technical requests really are at the head of
        // the same batches as the Legal ones; BatchSize 1 makes each early batch Technical only.
        var connectionString = fixture.NewDatabaseConnectionString();
        var time = ApiHost.NewClock();
        var interval = TimeSpan.FromSeconds(30);
        await using var api = ApiHost.Start(
            connectionString, time, interval, new Dictionary<string, string?> { ["Queue:BatchSize"] = "1" });
        await api.Client.CreateExpertAsync(5, [Subject.Legal], Ct);

        var db = new DbInspector(connectionString);
        var start = time.GetUtcNow().UtcDateTime;
        var technical1 = await db.InsertPendingAsync(Subject.Technical, Priority.Urgent, start.AddMinutes(-3), Ct);
        var technical2 = await db.InsertPendingAsync(Subject.Technical, Priority.Urgent, start.AddMinutes(-2), Ct);
        var legal = await db.InsertPendingAsync(Subject.Legal, Priority.Normal, start.AddMinutes(-1), Ct);

        time.Advance(interval);

        var status = await Eventually.WaitForAsync(
            async () => (await api.Client.GetRequestAsync(legal, Ct)).Data.Status,
            s => s == RequestStatus.Assigned,
            Ct);
        Assert.Equal(RequestStatus.Assigned, status);
        Assert.Equal(RequestStatus.Pending, (await api.Client.GetRequestAsync(technical1, Ct)).Data.Status);
        Assert.Equal(RequestStatus.Pending, (await api.Client.GetRequestAsync(technical2, Ct)).Data.Status);
    }
}
