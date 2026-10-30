using AssignHub.Application.Queue;
using Microsoft.Extensions.Options;

namespace AssignHub.Api.Queue;

// SPEC §7.5: recovery only, for an inline drain that never ran or failed. Runs on every instance;
// TryAssign makes that safe. It must never crash the host.
internal sealed partial class QueueSweeper(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<QueueOptions> options,
    ILogger<QueueSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.SweepInterval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SweepAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<QueueDrainer>().DrainAllAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSweepFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Queue sweep failed; retrying on the next interval.")]
    private partial void LogSweepFailed(Exception exception);
}
