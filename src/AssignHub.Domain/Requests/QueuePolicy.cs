using AssignHub.Domain.Enums;

namespace AssignHub.Domain.Requests;

// SPEC §7.1: effectively urgent first, then oldest, then lowest Id.
// The drain query (§7.4) applies the same order in SQL with CreatedAt <= AgingCutoff.
public static class QueuePolicy
{
    public static DateTime AgingCutoff(DateTime now, TimeSpan agingThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(agingThreshold, TimeSpan.Zero);
        return now - agingThreshold;
    }

    public static bool IsEffectivelyUrgent(QueueEntry entry, DateTime now, TimeSpan agingThreshold) =>
        entry.Priority == Priority.Urgent || entry.CreatedAt <= AgingCutoff(now, agingThreshold);

    public static IReadOnlyList<QueueEntry> Order(
        IEnumerable<QueueEntry> entries,
        DateTime now,
        TimeSpan agingThreshold)
    {
        var cutoff = AgingCutoff(now, agingThreshold);

        return entries
            .OrderByDescending(e => e.Priority == Priority.Urgent || e.CreatedAt <= cutoff)
            .ThenBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToList();
    }
}
