using AssignHub.Domain.Enums;
using AssignHub.Domain.Experts;
using AssignHub.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace AssignHub.Api.Startup;

internal static partial class DatabaseStartup
{
    // SPEC §10: migrate when enabled; seed demo experts in Development only, idempotently.
    public static async Task InitializeDatabaseAsync(this WebApplication app, CancellationToken cancellationToken)
    {
        var options = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var seed = options.SeedDemoData && app.Environment.IsDevelopment();
        if (!options.ApplyMigrationsOnStartup && !seed)
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();

        if (options.ApplyMigrationsOnStartup)
        {
            await initializer.MigrateAsync(cancellationToken);
        }

        if (seed)
        {
            var now = app.Services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
            if (await initializer.SeedExpertsIfEmptyAsync(DemoExperts(now), cancellationToken))
            {
                LogSeeded(app.Logger);
            }
        }
    }

    private static Expert[] DemoExperts(DateTime now) =>
    [
        Expert.Create("Financial Expert A", 4, [Subject.Financial], isActive: true, now),
        Expert.Create("Financial Expert B", 10, [Subject.Financial], isActive: true, now),
        Expert.Create("Legal Expert", 5, [Subject.Legal], isActive: true, now),
        Expert.Create("Technical and Legal Expert", 3, [Subject.Technical, Subject.Legal], isActive: true, now),
        Expert.Create("Inactive Technical Expert", 5, [Subject.Technical], isActive: false, now),
    ];

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded demo experts.")]
    private static partial void LogSeeded(ILogger logger);
}
