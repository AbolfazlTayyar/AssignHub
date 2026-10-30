using AssignHub.Domain.Requests;

namespace AssignHub.Application.Abstractions;

public interface IRequestStore
{
    // SPEC §6.2: inserts first; a RequestKey unique violation returns the existing row instead.
    Task<AddRequestResult> TryAddAsync(Request request, CancellationToken cancellationToken);

    Task<Request?> GetAsync(int id, CancellationToken cancellationToken);
}

public sealed record AddRequestResult(bool Added, Request Request)
{
    public static AddRequestResult Inserted(Request request) => new(true, request);

    public static AddRequestResult Duplicate(Request existing) => new(false, existing);
}
