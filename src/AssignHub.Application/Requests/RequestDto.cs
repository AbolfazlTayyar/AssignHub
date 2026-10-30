using AssignHub.Domain.Enums;
using AssignHub.Domain.Requests;

namespace AssignHub.Application.Requests;

public sealed record RequestDto(
    int Id,
    string RequestKey,
    string Title,
    string? Description,
    Subject Subject,
    Priority Priority,
    RequestStatus Status,
    int? ExpertId,
    DateTime CreatedAt,
    DateTime? AssignedAt,
    DateTime? ClosedAt)
{
    public static RequestDto From(Request request) => new(
        request.Id,
        request.RequestKey,
        request.Title,
        request.Description,
        request.Subject,
        request.Priority,
        request.Status,
        request.ExpertId,
        request.CreatedAt,
        request.AssignedAt,
        request.ClosedAt);
}
