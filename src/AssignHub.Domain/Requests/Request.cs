using AssignHub.Domain.Enums;

namespace AssignHub.Domain.Requests;

// SPEC §4.5: Status, ExpertId, AssignedAt and ClosedAt change only through the conditional
// SQL updates of assign (§6.1) and close (§9.1), so there is deliberately no Close() here.
public sealed class Request
{
    public const int MaxRequestKeyLength = 100;
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 2000;

    private Request()
    {
    }

    public int Id { get; private set; }

    public string RequestKey { get; private set; } = null!;

    public string Title { get; private set; } = null!;

    public string? Description { get; private set; }

    public Subject Subject { get; private set; }

    public Priority Priority { get; private set; }

    public RequestStatus Status { get; private set; }

    public int? ExpertId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? AssignedAt { get; private set; }

    public DateTime? ClosedAt { get; private set; }

    public static Request Create(
        string requestKey,
        string title,
        string? description,
        Subject subject,
        Priority priority,
        DateTime now)
    {
        if (string.IsNullOrWhiteSpace(requestKey))
        {
            throw new DomainException("RequestKey is required.");
        }

        if (requestKey.Length > MaxRequestKeyLength)
        {
            throw new DomainException($"RequestKey must be at most {MaxRequestKeyLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Title is required.");
        }

        if (title.Length > MaxTitleLength)
        {
            throw new DomainException($"Title must be at most {MaxTitleLength} characters.");
        }

        if (description?.Length > MaxDescriptionLength)
        {
            throw new DomainException($"Description must be at most {MaxDescriptionLength} characters.");
        }

        if (!Enum.IsDefined(subject))
        {
            throw new DomainException("Subject is unknown.");
        }

        if (!Enum.IsDefined(priority))
        {
            throw new DomainException("Priority is unknown.");
        }

        return new Request
        {
            RequestKey = requestKey,
            Title = title,
            Description = description,
            Subject = subject,
            Priority = priority,
            Status = RequestStatus.Pending,
            ExpertId = null,
            CreatedAt = now,
            AssignedAt = null,
            ClosedAt = null,
        };
    }

    // SPEC §6.2: decides between the 200 replay and the 409 conflict for a reused RequestKey.
    public bool HasSamePayload(string title, string? description, Subject subject, Priority priority) =>
        string.Equals(Title, title, StringComparison.Ordinal)
        && string.Equals(Description, description, StringComparison.Ordinal)
        && Subject == subject
        && Priority == priority;
}
