using AssignHub.Application.Abstractions;
using AssignHub.Domain.Enums;
using AssignHub.Domain.Experts;
using Microsoft.EntityFrameworkCore;

namespace AssignHub.IntegrationTests.Infrastructure;

// SPEC §5.5: SQL orders and limits the candidate load; ExpertSelector is the reference order it must match.
public sealed class CandidatePoolTests(SqlServerFixture fixture)
{
    private static readonly Dictionary<string, string?> PoolOfOne = new()
    {
        ["Assignment:CandidatePoolSize"] = "1",
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // With K = 1, SQL alone picks the candidate, so every round checks that the SQL order equals
    // ExpertSelector over all experts. The seeds hit every §5.2 level: 333/1000 < 332/997 < 1/3 (≈1e-6
    // apart), 1/3 = 2/6 (free slots decide), and three 1/2 experts split by NULL-first LRU and then Id.
    [Fact]
    public async Task TryAssign_PoolOfOne_PicksWhatSelectorPicksFromAllExperts()
    {
        await using var h = await ServiceHarness.CreateAsync(fixture, Ct, PoolOfOne);
        await AddLegalExpertAsync(h, capacity: 1, openCount: 0);
        await AddLegalExpertAsync(h, capacity: 3, openCount: 1);
        await AddLegalExpertAsync(h, capacity: 6, openCount: 2);
        await AddLegalExpertAsync(h, capacity: 1000, openCount: 333);
        await AddLegalExpertAsync(h, capacity: 997, openCount: 332);
        await AddLegalExpertAsync(h, capacity: 2, openCount: 1);
        var earlier = h.Now - TimeSpan.FromMinutes(5);
        await AddLegalExpertAsync(h, capacity: 2, openCount: 1, lastAssignedAt: earlier);
        await AddLegalExpertAsync(h, capacity: 2, openCount: 1, lastAssignedAt: earlier);
        await h.AddExpertAsync(5, [Subject.Legal], isActive: false, Ct);
        await h.AddExpertAsync(5, [Subject.Technical], isActive: true, Ct);

        for (var round = 0; round < 40; round++)
        {
            h.Time.Advance(TimeSpan.FromMinutes(1));
            var expected = ExpertSelector.Order(await LoadAllSnapshotsAsync(h), Subject.Legal)[0];
            var requestId = await h.AddRequestAsync(Subject.Legal, Priority.Normal, Ct);

            h.Sql.Clear();
            var result = await h.TryAssignAsync(requestId, Ct);

            Assert.Equal(TryAssignResult.Assigned(expected.Id), result);
            var load = Assert.Single(h.Sql.Commands, c => c.Contains("ORDER BY", StringComparison.Ordinal));
            Assert.Contains("TOP(", load, StringComparison.Ordinal);
        }
    }

    // A small pool can starve a request under contention (SPEC §6.1), but never overfills an expert,
    // and a later pass assigns whatever was left Pending.
    [Fact]
    public async Task TryAssign_PoolOfOneConcurrently_NeverExceedsCapacity()
    {
        for (var iteration = 0; iteration < 5; iteration++)
        {
            await using var h = await ServiceHarness.CreateAsync(fixture, Ct, PoolOfOne);
            int[] capacities = [2, 2, 1];
            var expertIds = new List<int>();
            foreach (var capacity in capacities)
            {
                expertIds.Add(await h.AddExpertAsync(capacity, [Subject.Legal], isActive: true, Ct));
            }

            var requestIds = new List<int>();
            for (var i = 0; i < 20; i++)
            {
                requestIds.Add(await h.AddRequestAsync(Subject.Legal, Priority.Normal, Ct));
            }

            var results = await Task.WhenAll(requestIds.Select(id => h.TryAssignAsync(id, Ct)));

            Assert.InRange(results.Count(r => r.Outcome == TryAssignOutcome.Assigned), 1, 5);
            await h.AssertCountersConsistentAsync(Ct);

            foreach (var id in requestIds)
            {
                await h.TryAssignAsync(id, Ct);
            }

            for (var i = 0; i < expertIds.Count; i++)
            {
                Assert.Equal(capacities[i], (await h.GetExpertAsync(expertIds[i], Ct))!.OpenCount);
            }

            await h.AssertCountersConsistentAsync(Ct);
        }
    }

    private static async Task AddLegalExpertAsync(
        ServiceHarness h,
        int capacity,
        int openCount,
        DateTime? lastAssignedAt = null)
    {
        var id = await h.AddExpertAsync(capacity, [Subject.Legal], isActive: true, Ct);
        await h.SetOpenCountAsync(id, openCount, Ct);
        await h.SetLastAssignedAtAsync(id, lastAssignedAt, Ct);
    }

    private static Task<List<ExpertSnapshot>> LoadAllSnapshotsAsync(ServiceHarness h) =>
        h.DbAsync(async db => (await db.Experts
                .AsNoTracking()
                .Select(e => new
                {
                    e.Id,
                    e.Capacity,
                    e.OpenCount,
                    e.IsActive,
                    e.LastAssignedAt,
                    Specialties = e.Specialties.Select(s => s.Subject).ToList(),
                })
                .ToListAsync(Ct))
            .Select(r => new ExpertSnapshot(r.Id, r.Capacity, r.OpenCount, r.IsActive, r.LastAssignedAt, r.Specialties))
            .ToList());
}
