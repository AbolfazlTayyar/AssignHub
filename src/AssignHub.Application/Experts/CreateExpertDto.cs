using System.ComponentModel.DataAnnotations;
using AssignHub.Domain.Enums;

namespace AssignHub.Application.Experts;

// SPEC §4.5 / §8.3: annotations check shape only; OpenCount and LastAssignedAt are not accepted at all.
public sealed record CreateExpertDto
{
    [Required]
    public string? Name { get; init; }

    [Required]
    public int? Capacity { get; init; }

    [Required]
    public IReadOnlyList<Subject>? Specialties { get; init; }

    public bool IsActive { get; init; } = true;
}
