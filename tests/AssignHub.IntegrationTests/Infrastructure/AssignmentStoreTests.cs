using AssignHub.Application.Abstractions;
using AssignHub.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace AssignHub.IntegrationTests.Infrastructure;

public sealed class AssignmentStoreTests(SqlServerFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TryAssign_TwentyConcurrent_CapacityFive_AssignsExactlyFive()
    {
        for (var iteration = 0; iteration < 5; iteration++)
        {
            await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
            var expertId = await h.AddExpertAsync(5, [Subject.Legal], isActive: true, Ct);
            var requestIds = new List<int>();
            for (var i = 0; i < 20; i++)
            {
                requestIds.Add(await h.AddRequestAsync(Subject.Legal, Priority.Normal, Ct));
            }

            var results = await Task.WhenAll(requestIds.Select(id => h.TryAssignAsync(id, Ct)));

            Assert.Equal(5, results.Count(r => r.Outcome == TryAssignOutcome.Assigned));
            Assert.Equal(15, results.Count(r => r.Outcome == TryAssignOutcome.NoCandidate));
            Assert.Equal(5, (await h.GetExpertAsync(expertId, Ct))!.OpenCount);
            await h.AssertCountersConsistentAsync(Ct);
        }
    }

    [Fact]
    public async Task TryAssign_SameRequestConcurrently_AssignsOnce()
    {
        for (var iteration = 0; iteration < 5; iteration++)
        {
            await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
            await h.AddExpertAsync(5, [Subject.Financial], isActive: true, Ct);
            await h.AddExpertAsync(5, [Subject.Financial], isActive: true, Ct);
            var requestId = await h.AddRequestAsync(Subject.Financial, Priority.Urgent, Ct);

            var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => h.TryAssignAsync(requestId, Ct)));

            Assert.Single(results, r => r.Outcome == TryAssignOutcome.Assigned);
            Assert.All(
                results.Where(r => r.Outcome != TryAssignOutcome.Assigned),
                r => Assert.Equal(TryAssignOutcome.AlreadyHandled, r.Outcome));
            await h.AssertCountersConsistentAsync(Ct);
        }
    }

    [Fact]
    public async Task TryAssign_TaskExample_PicksLowestRatio()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        var e1 = await h.AddExpertAsync(4, [Subject.Financial], isActive: true, Ct);
        var e2 = await h.AddExpertAsync(10, [Subject.Financial], isActive: true, Ct);
        await h.SetOpenCountAsync(e1, 2, Ct);
        await h.SetOpenCountAsync(e2, 3, Ct);
        var requestId = await h.AddRequestAsync(Subject.Financial, Priority.Normal, Ct);

        var result = await h.TryAssignAsync(requestId, Ct);

        Assert.Equal(TryAssignResult.Assigned(e2), result);
        var request = await h.GetRequestAsync(requestId, Ct);
        Assert.Equal(RequestStatus.Assigned, request!.Status);
        Assert.Equal(e2, request.ExpertId);
        Assert.Equal(h.Now, request.AssignedAt);
        Assert.Equal(DateTimeKind.Utc, request.AssignedAt!.Value.Kind);
        Assert.Equal(h.Now, (await h.GetExpertAsync(e2, Ct))!.LastAssignedAt);
    }

    [Fact]
    public async Task TryAssign_NoEligibleExpert_LeavesRequestPending()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        await h.AddExpertAsync(5, [Subject.Technical], isActive: false, Ct);
        await h.AddExpertAsync(5, [Subject.Legal], isActive: true, Ct);
        var full = await h.AddExpertAsync(1, [Subject.Technical], isActive: true, Ct);
        await h.SetOpenCountAsync(full, 1, Ct);
        var requestId = await h.AddRequestAsync(Subject.Technical, Priority.Urgent, Ct);

        var result = await h.TryAssignAsync(requestId, Ct);

        Assert.Equal(TryAssignResult.NoCandidate, result);
        var request = await h.GetRequestAsync(requestId, Ct);
        Assert.Equal(RequestStatus.Pending, request!.Status);
        Assert.Null(request.ExpertId);
    }

    [Fact]
    public async Task TryAssign_AlreadyAssigned_ReturnsAlreadyHandled()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        await h.AddExpertAsync(5, [Subject.Legal], isActive: true, Ct);
        var requestId = await h.AddRequestAsync(Subject.Legal, Priority.Normal, Ct);
        await h.TryAssignAsync(requestId, Ct);

        Assert.Equal(TryAssignResult.AlreadyHandled, await h.TryAssignAsync(requestId, Ct));
        await h.AssertCountersConsistentAsync(Ct);
    }

    [Fact]
    public async Task Close_Assigned_ReleasesSlotAndIsIdempotent()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        var expertId = await h.AddExpertAsync(1, [Subject.Legal], isActive: true, Ct);
        var requestId = await h.AddRequestAsync(Subject.Legal, Priority.Normal, Ct);
        await h.TryAssignAsync(requestId, Ct);
        h.Time.Advance(TimeSpan.FromMinutes(3));

        var first = await h.CloseAsync(requestId, Ct);
        var second = await h.CloseAsync(requestId, Ct);

        Assert.Equal(new CloseResult(CloseOutcome.Closed, expertId), first);
        Assert.Equal(new CloseResult(CloseOutcome.AlreadyClosed, expertId), second);
        var request = await h.GetRequestAsync(requestId, Ct);
        Assert.Equal(RequestStatus.Closed, request!.Status);
        Assert.Equal(expertId, request.ExpertId);
        Assert.Equal(h.Now, request.ClosedAt);
        Assert.Equal(0, (await h.GetExpertAsync(expertId, Ct))!.OpenCount);
        await h.AssertCountersConsistentAsync(Ct);
    }

    [Fact]
    public async Task Close_PendingOrMissing_IsRejected()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        var requestId = await h.AddRequestAsync(Subject.Legal, Priority.Normal, Ct);

        Assert.Equal(new CloseResult(CloseOutcome.NotAssigned), await h.CloseAsync(requestId, Ct));
        Assert.Equal(new CloseResult(CloseOutcome.NotFound), await h.CloseAsync(requestId + 1000, Ct));
    }

    [Fact]
    public async Task ConcurrentClosesAndAssigns_KeepCountersConsistent()
    {
        for (var iteration = 0; iteration < 3; iteration++)
        {
            await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
            await h.AddExpertAsync(3, [Subject.Legal], isActive: true, Ct);
            await h.AddExpertAsync(2, [Subject.Legal, Subject.Financial], isActive: true, Ct);

            var initial = new List<int>();
            for (var i = 0; i < 5; i++)
            {
                var id = await h.AddRequestAsync(Subject.Legal, Priority.Normal, Ct);
                await h.TryAssignAsync(id, Ct);
                initial.Add(id);
            }

            var waiting = new List<int>();
            for (var i = 0; i < 15; i++)
            {
                waiting.Add(await h.AddRequestAsync(Subject.Legal, Priority.Urgent, Ct));
            }

            var closes = initial.Select(id => (Task)h.CloseAsync(id, Ct));
            var assigns = waiting.Select(id => (Task)h.TryAssignAsync(id, Ct));
            await Task.WhenAll(closes.Concat(assigns));

            await h.AssertCountersConsistentAsync(Ct);
        }
    }

    [Fact]
    public async Task GetPendingBatch_OrdersByEffectiveUrgencyThenAgeThenId()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        var agedNormal = await h.AddRequestAsync(Subject.Legal, Priority.Normal, Ct);   // 09:00
        h.Time.Advance(TimeSpan.FromMinutes(2));
        var otherSubject = await h.AddRequestAsync(Subject.Technical, Priority.Urgent, Ct); // 09:02
        h.Time.Advance(TimeSpan.FromMinutes(3));
        var youngNormal = await h.AddRequestAsync(Subject.Legal, Priority.Normal, Ct);  // 09:05
        h.Time.Advance(TimeSpan.FromMinutes(2));
        var urgent = await h.AddRequestAsync(Subject.Financial, Priority.Urgent, Ct);   // 09:07
        h.Time.Advance(TimeSpan.FromMinutes(5));                                         // now 09:12

        var cutoff = h.Now - TimeSpan.FromMinutes(10);
        var batch = await h.RunAsync(sp => sp.GetRequiredService<IAssignmentStore>()
            .GetPendingBatchAsync([Subject.Legal, Subject.Financial], cutoff, 10, Ct));

        Assert.Equal([agedNormal, urgent, youngNormal], batch.Select(r => r.Id));
        Assert.DoesNotContain(batch, r => r.Id == otherSubject);
        Assert.Equal(Subject.Financial, batch.Single(r => r.Id == urgent).Subject);

        var limited = await h.RunAsync(sp => sp.GetRequiredService<IAssignmentStore>()
            .GetPendingBatchAsync([Subject.Legal, Subject.Financial], cutoff, 2, Ct));
        Assert.Equal([agedNormal, urgent], limited.Select(r => r.Id));
    }

    [Fact]
    public async Task GetPendingBatch_SkipsAssignedRequests()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct);
        await h.AddExpertAsync(1, [Subject.Legal], isActive: true, Ct);
        var assigned = await h.AddRequestAsync(Subject.Legal, Priority.Urgent, Ct);
        var pending = await h.AddRequestAsync(Subject.Legal, Priority.Urgent, Ct);
        await h.TryAssignAsync(assigned, Ct);

        var batch = await h.RunAsync(sp => sp.GetRequiredService<IAssignmentStore>()
            .GetPendingBatchAsync([Subject.Legal], h.Now, 10, Ct));

        Assert.Equal([pending], batch.Select(r => r.Id));
    }
}
