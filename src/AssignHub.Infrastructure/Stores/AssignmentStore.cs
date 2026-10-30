using AssignHub.Application.Abstractions;
using AssignHub.Domain.Enums;
using AssignHub.Domain.Experts;
using AssignHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssignHub.Infrastructure.Stores;

internal sealed partial class AssignmentStore(
    AssignHubDbContext db,
    TimeProvider timeProvider,
    IOptions<AssignmentOptions> options,
    ILogger<AssignmentStore> logger) : IAssignmentStore
{
    private enum ClaimOutcome
    {
        Assigned,
        ExpertUnavailable,
        RequestTaken,
    }

    // SPEC §6.1. The candidate order is only a preference; the conditional UPDATEs decide every race.
    public async Task<TryAssignResult> TryAssignAsync(int requestId, CancellationToken cancellationToken)
    {
        var request = await db.Requests
            .AsNoTracking()
            .Where(r => r.Id == requestId)
            .Select(r => new { r.Subject, r.Status })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Request {requestId} does not exist.");

        if (request.Status != RequestStatus.Pending)
        {
            return TryAssignResult.AlreadyHandled;
        }

        for (var attempt = 0; attempt < options.Value.MaxCandidateRefreshes; attempt++)
        {
            var candidates = ExpertSelector.Order(
                await LoadCandidatesAsync(request.Subject, cancellationToken),
                request.Subject);

            if (candidates.Count == 0)
            {
                return TryAssignResult.NoCandidate;
            }

            foreach (var expert in candidates)
            {
                switch (await ClaimAsync(requestId, expert.Id, request.Subject, cancellationToken))
                {
                    case ClaimOutcome.Assigned:
                        return TryAssignResult.Assigned(expert.Id);
                    case ClaimOutcome.RequestTaken:
                        return TryAssignResult.AlreadyHandled;
                }
            }
        }

        return TryAssignResult.NoCandidate;
    }

    // SPEC §9.1. Lock order is Requests → Experts, the reverse tables of assign but never the same rows.
    // ExpertId is written in the same statement that makes a request Assigned and never changes after
    // that, so reading it first is safe; the conditional UPDATE still decides who closes the request.
    public async Task<CloseResult> CloseAsync(int requestId, CancellationToken cancellationToken)
    {
        var current = await db.Requests
            .AsNoTracking()
            .Where(r => r.Id == requestId)
            .Select(r => new { r.Status, r.ExpertId })
            .SingleOrDefaultAsync(cancellationToken);

        switch (current)
        {
            case null:
                return new CloseResult(CloseOutcome.NotFound);
            case { Status: RequestStatus.Closed }:
                return new CloseResult(CloseOutcome.AlreadyClosed, current.ExpertId);
            case { Status: RequestStatus.Pending }:
                return new CloseResult(CloseOutcome.NotAssigned);
        }

        var expertId = current.ExpertId!.Value;
        var strategy = db.Database.CreateExecutionStrategy();
        var closedHere = await strategy.ExecuteAsync(
            async ct =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var now = timeProvider.GetUtcNow().UtcDateTime;

                var closed = await db.Requests
                    .Where(r => r.Id == requestId && r.Status == RequestStatus.Assigned)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(r => r.Status, RequestStatus.Closed)
                            .SetProperty(r => r.ClosedAt, now),
                        ct);

                if (closed == 0)
                {
                    await transaction.RollbackAsync(ct);
                    return false;
                }

                var released = await db.Experts
                    .Where(e => e.Id == expertId && e.OpenCount > 0)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.OpenCount, e => e.OpenCount - 1), ct);

                if (released == 0)
                {
                    LogOpenCountAlreadyZero(expertId, requestId);
                }

                await transaction.CommitAsync(ct);
                return true;
            },
            cancellationToken);

        // Assigned can only move to Closed, so losing the UPDATE means a concurrent close won.
        return new CloseResult(closedHere ? CloseOutcome.Closed : CloseOutcome.AlreadyClosed, expertId);
    }

    // SPEC §7.4. Status and Priority are constants, so EF inlines them as N'...' literals, which lets the
    // filtered index IX_Requests_Queue match. Priority is ordered by an explicit CASE, never by its text.
    public async Task<IReadOnlyList<QueuedRequest>> GetPendingBatchAsync(
        IReadOnlyCollection<Subject> subjects,
        DateTime agingCutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var subjectList = subjects.ToList();

        return await db.Requests
            .AsNoTracking()
            .Where(r => r.Status == RequestStatus.Pending && subjectList.Contains(r.Subject))
            .OrderByDescending(r => r.Priority == Priority.Urgent || r.CreatedAt <= agingCutoff ? 1 : 0)
            .ThenBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Take(batchSize)
            .Select(r => new QueuedRequest(r.Id, r.Subject))
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ExpertSnapshot>> LoadCandidatesAsync(
        Subject subject,
        CancellationToken cancellationToken)
    {
        var rows = await db.Experts
            .AsNoTracking()
            .Where(e => e.IsActive
                && e.OpenCount < e.Capacity
                && e.Specialties.Any(s => s.Subject == subject))
            .Select(e => new
            {
                e.Id,
                e.Capacity,
                e.OpenCount,
                e.IsActive,
                e.LastAssignedAt,
                Specialties = e.Specialties.Select(s => s.Subject).ToList(),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ExpertSnapshot(r.Id, r.Capacity, r.OpenCount, r.IsActive, r.LastAssignedAt, r.Specialties))
            .ToList();
    }

    private async Task<ClaimOutcome> ClaimAsync(
        int requestId,
        int expertId,
        Subject subject,
        CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            async ct =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var now = timeProvider.GetUtcNow().UtcDateTime;

                // (a) The UPDATE lock re-evaluates OpenCount < Capacity against the latest committed row,
                // so at most Capacity − OpenCount concurrent reservations succeed.
                var reserved = await db.Experts
                    .Where(e => e.Id == expertId
                        && e.IsActive
                        && e.OpenCount < e.Capacity
                        && e.Specialties.Any(s => s.Subject == subject))
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(e => e.OpenCount, e => e.OpenCount + 1)
                            .SetProperty(e => e.LastAssignedAt, now),
                        ct);

                if (reserved == 0)
                {
                    await transaction.RollbackAsync(ct);
                    return ClaimOutcome.ExpertUnavailable;
                }

                // (b) Only one claimer can move the request out of Pending; a loser gives its slot back.
                var claimed = await db.Requests
                    .Where(r => r.Id == requestId && r.Status == RequestStatus.Pending)
                    .ExecuteUpdateAsync(
                        s => s
                            .SetProperty(r => r.Status, RequestStatus.Assigned)
                            .SetProperty(r => r.ExpertId, expertId)
                            .SetProperty(r => r.AssignedAt, now),
                        ct);

                if (claimed == 0)
                {
                    await transaction.RollbackAsync(ct);
                    return ClaimOutcome.RequestTaken;
                }

                await transaction.CommitAsync(ct);
                return ClaimOutcome.Assigned;
            },
            cancellationToken);
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Closed request {RequestId} but expert {ExpertId} already had OpenCount 0.")]
    private partial void LogOpenCountAlreadyZero(int expertId, int requestId);
}
