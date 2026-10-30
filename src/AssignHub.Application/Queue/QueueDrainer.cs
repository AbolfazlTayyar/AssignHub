using AssignHub.Application.Abstractions;
using AssignHub.Domain.Enums;
using AssignHub.Domain.Requests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssignHub.Application.Queue;

public sealed partial class QueueDrainer(
    IAssignmentStore assignments,
    TimeProvider timeProvider,
    IOptions<QueueOptions> options,
    ILogger<QueueDrainer> logger)
{
    public Task DrainAllAsync(CancellationToken cancellationToken) =>
        DrainAsync(Enum.GetValues<Subject>(), cancellationToken);

    // SPEC §7.4.
    public async Task DrainAsync(IReadOnlyCollection<Subject> subjects, CancellationToken cancellationToken)
    {
        var exhausted = new HashSet<Subject>();

        while (true)
        {
            var active = subjects.Distinct().Where(s => !exhausted.Contains(s)).ToList();
            if (active.Count == 0)
            {
                return;
            }

            var cutoff = QueuePolicy.AgingCutoff(timeProvider.GetUtcNow().UtcDateTime, options.Value.AgingThreshold);
            var batch = await assignments.GetPendingBatchAsync(active, cutoff, options.Value.BatchSize, cancellationToken);
            if (batch.Count == 0)
            {
                return;
            }

            var progress = false;
            foreach (var request in batch)
            {
                if (exhausted.Contains(request.Subject))
                {
                    continue;
                }

                var result = await assignments.TryAssignAsync(request.Id, cancellationToken);
                switch (result.Outcome)
                {
                    case TryAssignOutcome.Assigned:
                        progress = true;
                        break;
                    case TryAssignOutcome.NoCandidate:
                        exhausted.Add(request.Subject);
                        progress = true;
                        break;
                }
            }

            if (!progress)
            {
                return;
            }
        }
    }

    // SPEC §7.5: inline drains run after the triggering commit. If one fails, the committed change still
    // stands and the QueueSweeper recovers the queue, so the caller's response must not turn into a 500.
    public async Task TryDrainAsync(IReadOnlyCollection<Subject> subjects, CancellationToken cancellationToken)
    {
        try
        {
            await DrainAsync(subjects, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogInlineDrainFailed(ex, string.Join(", ", subjects));
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Inline queue drain for {Subjects} failed; the sweeper will retry.")]
    private partial void LogInlineDrainFailed(Exception exception, string subjects);
}
