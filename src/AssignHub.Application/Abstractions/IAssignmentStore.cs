using AssignHub.Domain.Enums;

namespace AssignHub.Application.Abstractions;

// The contended writes (SPEC §6.1, §9.1) and the queue read (§7.4). All arbitration happens in the database.
public interface IAssignmentStore
{
    Task<TryAssignResult> TryAssignAsync(int requestId, CancellationToken cancellationToken);

    Task<CloseResult> CloseAsync(int requestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<QueuedRequest>> GetPendingBatchAsync(
        IReadOnlyCollection<Subject> subjects,
        DateTime agingCutoff,
        int batchSize,
        CancellationToken cancellationToken);
}

public enum TryAssignOutcome
{
    Assigned = 1,
    NoCandidate = 2,
    AlreadyHandled = 3,
}

public sealed record TryAssignResult(TryAssignOutcome Outcome, int? ExpertId = null)
{
    public static TryAssignResult Assigned(int expertId) => new(TryAssignOutcome.Assigned, expertId);

    public static readonly TryAssignResult NoCandidate = new(TryAssignOutcome.NoCandidate);

    public static readonly TryAssignResult AlreadyHandled = new(TryAssignOutcome.AlreadyHandled);
}

public enum CloseOutcome
{
    Closed = 1,
    AlreadyClosed = 2,
    NotAssigned = 3,
    NotFound = 4,
}

public sealed record CloseResult(CloseOutcome Outcome, int? ExpertId = null);

public sealed record QueuedRequest(int Id, Subject Subject);
