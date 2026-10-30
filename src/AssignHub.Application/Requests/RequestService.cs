using AssignHub.Application.Abstractions;
using AssignHub.Application.Common;
using AssignHub.Application.Queue;
using AssignHub.Domain.Requests;

namespace AssignHub.Application.Requests;

public sealed class RequestService(
    IRequestStore requests,
    IExpertStore experts,
    IAssignmentStore assignments,
    QueueDrainer drainer,
    TimeProvider timeProvider)
{
    // SPEC §6.2.
    public async Task<Result<RequestDto>> CreateAsync(CreateRequestDto dto, CancellationToken cancellationToken)
    {
        var request = Request.Create(
            dto.RequestKey!,
            dto.Title!,
            dto.Description,
            dto.Subject!.Value,
            dto.Priority!.Value,
            timeProvider.GetUtcNow().UtcDateTime);

        var added = await requests.TryAddAsync(request, cancellationToken);
        if (!added.Added)
        {
            var existing = added.Request;
            return existing.HasSamePayload(request.Title, request.Description, request.Subject, request.Priority)
                ? Result<RequestDto>.Ok(RequestDto.From(existing))
                : Result<RequestDto>.Conflict(
                    $"RequestKey '{request.RequestKey}' was already used with a different payload.");
        }

        await drainer.TryDrainAsync([request.Subject], cancellationToken);

        return Result<RequestDto>.Created(await GetRequiredAsync(request.Id, cancellationToken));
    }

    public async Task<Result<RequestDto>> GetAsync(int id, CancellationToken cancellationToken)
    {
        var request = await requests.GetAsync(id, cancellationToken);
        return request is null
            ? Result<RequestDto>.NotFound(NotFoundMessage(id))
            : Result<RequestDto>.Ok(RequestDto.From(request));
    }

    // SPEC §9.1. An already Closed request is a 200 so a client can safely retry; it drains again too,
    // which covers the retry after a lost response when the first drain never ran.
    public async Task<Result<RequestDto>> CloseAsync(int id, CancellationToken cancellationToken)
    {
        var result = await assignments.CloseAsync(id, cancellationToken);

        switch (result.Outcome)
        {
            case CloseOutcome.NotFound:
                return Result<RequestDto>.NotFound(NotFoundMessage(id));
            case CloseOutcome.NotAssigned:
                return Result<RequestDto>.Conflict($"Request {id} is not assigned yet, so it cannot be closed.");
        }

        var expert = await experts.GetAsync(result.ExpertId!.Value, cancellationToken);
        if (expert is not null)
        {
            await drainer.TryDrainAsync(expert.Specialties.Select(s => s.Subject).ToList(), cancellationToken);
        }

        return Result<RequestDto>.Ok(await GetRequiredAsync(id, cancellationToken));
    }

    private async Task<RequestDto> GetRequiredAsync(int id, CancellationToken cancellationToken) =>
        RequestDto.From(await requests.GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Request {id} disappeared."));

    private static string NotFoundMessage(int id) => $"Request {id} was not found.";
}
