using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace AssignHub.IntegrationTests.Http;

// One in-process API instance. Several hosts on the same connection string behave like several
// deployed instances: they share nothing but the database.
internal sealed class ApiHost : IAsyncDisposable
{
    // Long enough that advancing the fake clock in a test never triggers a sweep by accident.
    public static readonly TimeSpan NoSweep = TimeSpan.FromDays(1);

    private readonly WebApplicationFactory<Program> _factory;

    private ApiHost(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        Client = new ApiClient(factory.CreateClient());
    }

    public ApiClient Client { get; }

    public static ApiHost Start(
        string connectionString,
        FakeTimeProvider time,
        TimeSpan? sweepInterval = null,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
            builder.UseSetting("Database:SeedDemoData", "false");
            builder.UseSetting("Queue:SweepInterval", (sweepInterval ?? NoSweep).ToString());
            builder.UseSetting("Logging:LogLevel:Default", "Warning");
            foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(time);
                configureServices?.Invoke(services);
            });
        });

        return new ApiHost(factory);
    }

    public static FakeTimeProvider NewClock() => new(new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero));

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
    }
}
