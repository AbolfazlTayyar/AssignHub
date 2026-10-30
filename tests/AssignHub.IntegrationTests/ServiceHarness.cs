using System.Collections.Concurrent;
using AssignHub.Application;
using AssignHub.Application.Abstractions;
using AssignHub.Application.Experts;
using AssignHub.Application.Requests;
using AssignHub.Domain.Enums;
using AssignHub.Domain.Experts;
using AssignHub.Domain.Requests;
using AssignHub.Infrastructure;
using AssignHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace AssignHub.IntegrationTests;

// Each call runs in its own DI scope (own DbContext and connection), like a separate HTTP request.
internal sealed class ServiceHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private ServiceHarness(ServiceProvider provider, FakeTimeProvider time, SqlCapture sql)
    {
        _provider = provider;
        Time = time;
        Sql = sql;
    }

    public FakeTimeProvider Time { get; }

    public SqlCapture Sql { get; }

    public static async Task<ServiceHarness> CreateAsync(
        SqlServerFixture fixture,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? settings = null)
    {
        var connectionString = await fixture.CreateDatabaseAsync(cancellationToken);
        var values = new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{AssignHub.Infrastructure.DependencyInjection.ConnectionStringName}"] = connectionString,
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero));
        var sql = new SqlCapture();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<TimeProvider>(time);
        services.AddLogging(b => b.AddProvider(sql));
        services.AddApplication(configuration);
        services.AddInfrastructure(configuration);

        return new ServiceHarness(services.BuildServiceProvider(validateScopes: true), time, sql);
    }

    public DateTime Now => Time.GetUtcNow().UtcDateTime;

    public async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public Task<T> DbAsync<T>(Func<AssignHubDbContext, Task<T>> action) =>
        RunAsync(sp => action(sp.GetRequiredService<AssignHubDbContext>()));

    public Task<T> RequestsAsync<T>(Func<RequestService, Task<T>> action) =>
        RunAsync(sp => action(sp.GetRequiredService<RequestService>()));

    public Task<T> ExpertsAsync<T>(Func<ExpertService, Task<T>> action) =>
        RunAsync(sp => action(sp.GetRequiredService<ExpertService>()));

    public Task<TryAssignResult> TryAssignAsync(int requestId, CancellationToken cancellationToken) =>
        RunAsync(sp => sp.GetRequiredService<IAssignmentStore>().TryAssignAsync(requestId, cancellationToken));

    public Task<CloseResult> CloseAsync(int requestId, CancellationToken cancellationToken) =>
        RunAsync(sp => sp.GetRequiredService<IAssignmentStore>().CloseAsync(requestId, cancellationToken));

    public Task<AddRequestResult> TryAddAsync(Request request, CancellationToken cancellationToken) =>
        RunAsync(sp => sp.GetRequiredService<IRequestStore>().TryAddAsync(request, cancellationToken));

    public Task<Request?> GetRequestAsync(int id, CancellationToken cancellationToken) =>
        RunAsync(sp => sp.GetRequiredService<IRequestStore>().GetAsync(id, cancellationToken));

    public Task<Expert?> GetExpertAsync(int id, CancellationToken cancellationToken) =>
        RunAsync(sp => sp.GetRequiredService<IExpertStore>().GetAsync(id, cancellationToken));

    public async Task<int> AddExpertAsync(
        int capacity,
        Subject[] specialties,
        bool isActive,
        CancellationToken cancellationToken)
    {
        var expert = Expert.Create($"Expert {Guid.NewGuid():N}", capacity, specialties, isActive, Now);
        await RunAsync(async sp =>
        {
            await sp.GetRequiredService<IExpertStore>().AddAsync(expert, cancellationToken);
            return expert.Id;
        });
        return expert.Id;
    }

    public async Task<int> AddRequestAsync(
        Subject subject,
        Priority priority,
        CancellationToken cancellationToken)
    {
        var request = Request.Create($"key-{Guid.NewGuid():N}", "Title", null, subject, priority, Now);
        var result = await TryAddAsync(request, cancellationToken);
        return result.Request.Id;
    }

    // Test setup only: production code never sets OpenCount outside the conditional updates.
    public Task<int> SetOpenCountAsync(int expertId, int openCount, CancellationToken cancellationToken) =>
        DbAsync(db => db.Experts
            .Where(e => e.Id == expertId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.OpenCount, openCount), cancellationToken));

    public Task AssertCountersConsistentAsync(CancellationToken cancellationToken) =>
        DbAsync(async db =>
        {
            await DbInspector.AssertCountersConsistentAsync(db, cancellationToken);
            return 0;
        });

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}

internal sealed class SqlCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _commands = new();

    public IReadOnlyCollection<string> Commands => _commands;

    public void Clear() => _commands.Clear();

    public ILogger CreateLogger(string categoryName) =>
        categoryName == DbLoggerCategory.Database.Command.Name ? new CommandLogger(_commands) : NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class CommandLogger(ConcurrentQueue<string> commands) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == RelationalEventId.CommandExecuted.Id)
            {
                commands.Enqueue(formatter(state, exception));
            }
        }
    }
}
