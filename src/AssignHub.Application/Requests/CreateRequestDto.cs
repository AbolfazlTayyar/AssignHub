using System.ComponentModel.DataAnnotations;
using AssignHub.Domain.Enums;

namespace AssignHub.Application.Requests;

// SPEC §4.5: annotations check shape only; lengths and other business rules live in Request.Create.
public sealed record CreateRequestDto
{
    [Required]
    public string? RequestKey { get; init; }

    [Required]
    public string? Title { get; init; }

    public string? Description { get; init; }

    [Required]
    public Subject? Subject { get; init; }

    [Required]
    public Priority? Priority { get; init; }
}
