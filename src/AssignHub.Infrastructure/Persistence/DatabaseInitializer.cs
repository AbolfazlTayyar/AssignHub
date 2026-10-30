using AssignHub.Domain.Experts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace AssignHub.Infrastructure.Persistence;

public sealed class DatabaseInitializer(AssignHubDbContext db)
{
    private const int LockTimeoutMilliseconds = 60_000;

    // SPEC §10: EF Core's migration lock serialises instances, but it lives inside the database, so it
    // cannot protect CREATE DATABASE itself. Two instances starting on an empty server would race to
    // create it, so creation is serialised first by a server-wide app lock held on a master connection.
    // EF Core has no API for application locks; that is the only raw SQL here.
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var creator = db.GetService<IRelationalDatabaseCreator>();
        var master = new SqlConnectionStringBuilder(db.Database.GetConnectionString()) { InitialCatalog = "master" };
        var resource = $"AssignHub:create-database:{db.Database.GetDbConnection().Database}";

        await using (var connection = new SqlConnection(master.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await ExecuteAsync(
                connection,
                """
                DECLARE @result int;
                EXEC @result = sp_getapplock
                    @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = @timeout;
                IF @result < 0 THROW 50000, 'Timed out waiting to create the database.', 1;
                """,
                resource,
                cancellationToken);
            try
            {
                if (!await creator.ExistsAsync(cancellationToken))
                {
                    await creator.CreateAsync(cancellationToken);
                }
            }
            finally
            {
                await ExecuteAsync(
                    connection,
                    "EXEC sp_releaseapplock @Resource = @resource, @LockOwner = 'Session';",
                    resource,
                    CancellationToken.None);
            }
        }

        await db.Database.MigrateAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        SqlConnection connection,
        string sql,
        string resource,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = LockTimeoutMilliseconds / 1000 + 30;
        command.Parameters.AddWithValue("@resource", resource);
        command.Parameters.AddWithValue("@timeout", LockTimeoutMilliseconds);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // UPDLOCK + HOLDLOCK range-locks the empty table, so a second instance waits, then sees rows and skips.
    // EF Core cannot express table hints, so this one locking read stays raw SQL.
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
