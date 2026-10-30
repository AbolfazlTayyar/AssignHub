using AssignHub.Domain.Enums;
using AssignHub.Domain.Requests;
using AssignHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssignHub.IntegrationTests;

// Direct database access for assertions and for simulating states the API never produces.
internal sealed class DbInspector(string connectionString)
{
    public async Task<T> QueryAsync<T>(Func<AssignHubDbContext, Task<T>> query)
    {
        var options = new DbContextOptionsBuilder<AssignHubDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new AssignHubDbContext(options);
        return await query(db);
    }

    public Task AssertCountersConsistentAsync(CancellationToken cancellationToken) =>
        QueryAsync(async db =>
        {
            await AssertCountersConsistentAsync(db, cancellationToken);
            return 0;
        });

    // ADR 0006: OpenCount must equal the number of Assigned requests and never exceed Capacity;
    // a Pending request never carries an expert.
    public static async Task AssertCountersConsistentAsync(AssignHubDbContext db, CancellationToken cancellationToken)
    {
        var experts = await db.Experts
            .Select(e => new
            {
                e.Id,
                e.Capacity,
                e.OpenCount,
                Assigned = db.Requests.Count(r => r.ExpertId == e.Id && r.Status == RequestStatus.Assigned),
            })
            .ToListAsync(cancellationToken);

        foreach (var expert in experts)
        {
            Assert.Equal(expert.Assigned, expert.OpenCount);
            Assert.InRange(expert.OpenCount, 0, expert.Capacity);
        }

        var pendingWithExpert = await db.Requests
            .CountAsync(r => r.Status == RequestStatus.Pending && r.ExpertId != null, cancellationToken);
        Assert.Equal(0, pendingWithExpert);
    }

    public Task<int> CountAsync(RequestStatus status, CancellationToken cancellationToken) =>
        QueryAsync(db => db.Requests.CountAsync(r => r.Status == status, cancellationToken));

    // Simulates a request whose inline drain never ran (e.g. the instance crashed after commit).
    public Task<int> InsertPendingAsync(
        Subject subject,
        Priority priority,
        DateTime createdAt,
        CancellationToken cancellationToken) =>
        QueryAsync(async db =>
        {
            var request = Request.Create($"direct-{Guid.NewGuid():N}", "Inserted directly", null, subject, priority, createdAt);
            db.Requests.Add(request);
            await db.SaveChangesAsync(cancellationToken);
            return request.Id;
        });
}
