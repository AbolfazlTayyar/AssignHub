using AssignHub.Infrastructure.Persistence;
using AssignHub.IntegrationTests;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace AssignHub.IntegrationTests;

// One SQL Server container per test run; every test gets its own freshly migrated database.
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    // The database does not exist yet; the API creates and migrates it on startup.
    public string NewDatabaseConnectionString() =>
        new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = $"AssignHub_{Guid.NewGuid():N}",
        }.ConnectionString;

    public async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = NewDatabaseConnectionString();

        var options = new DbContextOptionsBuilder<AssignHubDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new AssignHubDbContext(options);
        await db.Database.MigrateAsync(cancellationToken);

        return connectionString;
    }
}
