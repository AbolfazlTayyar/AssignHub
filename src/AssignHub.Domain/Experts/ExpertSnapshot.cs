using AssignHub.Domain.Enums;

namespace AssignHub.Domain.Experts;

public sealed record ExpertSnapshot(
    int Id,
    int Capacity,
    int OpenCount,
    bool IsActive,
    DateTime? LastAssignedAt,
    IReadOnlyCollection<Subject> Specialties)
{
    public int FreeSlots => Capacity - OpenCount;
}
