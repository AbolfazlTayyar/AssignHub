using AssignHub.Domain.Enums;

namespace AssignHub.Domain.Experts;

// SPEC §5: the first expert in the returned order is the one to try first.
// The order is only a preference; the capacity guarantee comes from the conditional UPDATE (§6.1).
public static class ExpertSelector
{
    public static IReadOnlyList<ExpertSnapshot> Order(IReadOnlyList<ExpertSnapshot> experts, Subject subject) =>
        experts
            .Where(e => IsEligible(e, subject))
            .OrderBy(e => e, Comparer<ExpertSnapshot>.Create(Compare))
            .ToList();

    public static bool IsEligible(ExpertSnapshot expert, Subject subject) =>
        expert.IsActive
        && expert.OpenCount < expert.Capacity
        && expert.Specialties.Contains(subject);

    private static int Compare(ExpertSnapshot? x, ExpertSnapshot? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        // SPEC §5.2.1: x.Open/x.Cap vs y.Open/y.Cap compared exactly by cross-multiplication.
        var byRatio = ((long)x.OpenCount * y.Capacity).CompareTo((long)y.OpenCount * x.Capacity);
        if (byRatio != 0)
        {
            return byRatio;
        }

        var byFreeSlots = y.FreeSlots.CompareTo(x.FreeSlots);
        if (byFreeSlots != 0)
        {
            return byFreeSlots;
        }

        // Nullable.Compare sorts null first, so a never-assigned expert wins (SPEC §5.2.3).
        var byLastAssigned = Nullable.Compare(x.LastAssignedAt, y.LastAssignedAt);
        if (byLastAssigned != 0)
        {
            return byLastAssigned;
        }

        return x.Id.CompareTo(y.Id);
    }
}
