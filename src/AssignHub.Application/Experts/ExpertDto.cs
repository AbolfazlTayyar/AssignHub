using AssignHub.Domain.Enums;
using AssignHub.Domain.Experts;

namespace AssignHub.Application.Experts;

public sealed record ExpertDto(
    int Id,
    string Name,
    int Capacity,
    int OpenCount,
    double LoadRatio,
    bool IsActive,
    IReadOnlyList<Subject> Specialties,
    DateTime? LastAssignedAt)
{
    // LoadRatio is for display only; selection compares ratios exactly (SPEC §5.2).
    public static ExpertDto From(Expert expert) => new(
        expert.Id,
        expert.Name,
        expert.Capacity,
        expert.OpenCount,
        (double)expert.OpenCount / expert.Capacity,
        expert.IsActive,
        expert.Specialties.Select(s => s.Subject).Order().ToList(),
        expert.LastAssignedAt);
}
