using AssignHub.Domain.Experts;
using Microsoft.EntityFrameworkCore;

namespace AssignHub.Infrastructure.Persistence;

public sealed class DatabaseInitializer(AssignHubDbContext db)
{
    // EF Core takes a database-wide migration lock, so instances starting together are safe (SPEC §10).
    public Task MigrateAsync(CancellationToken cancellationToken) => db.Database.MigrateAsync(cancellationToken);

    // UPDLOCK + HOLDLOCK range-locks the empty table, so a second instance waits, then sees rows and skips.
    public async Task<bool> SeedExpertsIfEmptyAsync(
        IReadOnlyCollection<Expert> experts,
        CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            async ct =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var existing = await db.Database
                    .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [Experts] WITH (UPDLOCK, HOLDLOCK)")
                    .SingleAsync(ct);

                if (existing > 0)
                {
                    await transaction.RollbackAsync(ct);
                    return false;
                }

                db.Experts.AddRange(experts);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return true;
            },
            cancellationToken);
    }
}
