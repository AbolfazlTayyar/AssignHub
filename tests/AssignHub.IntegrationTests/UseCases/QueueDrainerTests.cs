using AssignHub.Application.Queue;
using AssignHub.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace AssignHub.IntegrationTests.UseCases;

public sealed class QueueDrainerTests(SqlServerFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Drain_BatchFullOfUnservableSubject_StillAssignsOtherSubjects()
    {
        // SPEC §7.4 regression: the first batches hold only Technical requests, which no expert can take.
        await using var h = await ServiceHarness.CreateAsync(
            fixture, Ct, new Dictionary<string, string?> { ["Queue:BatchSize"] = "2" });
        await h.AddExpertAsync(1, [Subject.Legal], isActive: true, Ct);
        var technical = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            technical.Add(await h.AddRequestAsync(Subject.Technical, Priority.Urgent, Ct));
            h.Time.Advance(TimeSpan.FromSeconds(1));
        }

        var legal = await h.AddRequestAsync(Subject.Legal, Priority.Normal, Ct);

        await h.RunAsync(async sp =>
        {
            await sp.GetRequiredService<QueueDrainer>().DrainAllAsync(Ct);
            return 0;
        });

        Assert.Equal(RequestStatus.Assigned, (await h.GetRequestAsync(legal, Ct))!.Status);
        foreach (var id in technical)
        {
            Assert.Equal(RequestStatus.Pending, (await h.GetRequestAsync(id, Ct))!.Status);
        }
    }

    [Fact]
    public async Task Drain_FillsEveryFreeSlotInPriorityOrder()
    {
        await using var h = await ServiceHarness.CreateAsync(
            fixture, Ct, new Dictionary<string, string?> { ["Queue:BatchSize"] = "2" });
        var normal1 = await h.AddRequestAsync(Subject.Financial, Priority.Normal, Ct);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        var normal2 = await h.AddRequestAsync(Subject.Financial, Priority.Normal, Ct);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        var urgent1 = await h.AddRequestAsync(Subject.Financial, Priority.Urgent, Ct);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        var urgent2 = await h.AddRequestAsync(Subject.Financial, Priority.Urgent, Ct);
        await h.AddExpertAsync(3, [Subject.Financial], isActive: true, Ct);

        await h.RunAsync(async sp =>
        {
            await sp.GetRequiredService<QueueDrainer>().DrainAsync([Subject.Financial], Ct);
            return 0;
        });

        Assert.Equal(RequestStatus.Assigned, (await h.GetRequestAsync(urgent1, Ct))!.Status);
        Assert.Equal(RequestStatus.Assigned, (await h.GetRequestAsync(urgent2, Ct))!.Status);
        Assert.Equal(RequestStatus.Assigned, (await h.GetRequestAsync(normal1, Ct))!.Status);
        Assert.Equal(RequestStatus.Pending, (await h.GetRequestAsync(normal2, Ct))!.Status);
        await h.AssertCountersConsistentAsync(Ct);
    }
}
