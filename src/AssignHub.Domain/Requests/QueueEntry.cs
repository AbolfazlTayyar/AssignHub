using AssignHub.Domain.Enums;

namespace AssignHub.Domain.Requests;

public sealed record QueueEntry(int Id, Priority Priority, DateTime CreatedAt);
