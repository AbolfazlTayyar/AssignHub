using AssignHub.Application.Abstractions;
using AssignHub.Application.Common;
using AssignHub.Application.Queue;
using AssignHub.Domain.Experts;

namespace AssignHub.Application.Experts;

public sealed class ExpertService(IExpertStore experts, QueueDrainer drainer, TimeProvider timeProvider)
{
    public async Task<Result<ExpertDto>> CreateAsync(CreateExpertDto dto, CancellationToken cancellationToken)
    {
        var expert = Expert.Create(
            dto.Name!,
            dto.Capacity!.Value,
            dto.Specialties!,
            dto.IsActive,
            timeProvider.GetUtcNow().UtcDateTime);

        await experts.AddAsync(expert, cancellationToken);

        if (expert.IsActive)
        {
            await DrainSpecialtiesAsync(expert, cancellationToken);
        }

        return Result<ExpertDto>.Created(await GetRequiredAsync(expert.Id, cancellationToken));
    }

    public async Task<IReadOnlyList<ExpertDto>> ListAsync(CancellationToken cancellationToken) =>
        (await experts.ListAsync(cancellationToken)).Select(ExpertDto.From).ToList();

    public async Task<Result<ExpertDto>> GetAsync(int id, CancellationToken cancellationToken)
    {
        var expert = await experts.GetAsync(id, cancellationToken);
        return expert is null
            ? Result<ExpertDto>.NotFound(NotFoundMessage(id))
            : Result<ExpertDto>.Ok(ExpertDto.From(expert));
    }

    // SPEC §8.2.
    public async Task<Result<ExpertDto>> ActivateAsync(int id, CancellationToken cancellationToken)
    {
        var expert = await experts.FindForUpdateAsync(id, cancellationToken);
        if (expert is null)
        {
            return Result<ExpertDto>.NotFound(NotFoundMessage(id));
        }

        expert.Activate();
        await experts.SaveChangesAsync(cancellationToken);
        await DrainSpecialtiesAsync(expert, cancellationToken);

        return Result<ExpertDto>.Ok(await GetRequiredAsync(id, cancellationToken));
    }

    // SPEC §8.1: existing Assigned requests stay with the expert and can still be closed.
    public async Task<Result<ExpertDto>> DeactivateAsync(int id, CancellationToken cancellationToken)
    {
        var expert = await experts.FindForUpdateAsync(id, cancellationToken);
        if (expert is null)
        {
            return Result<ExpertDto>.NotFound(NotFoundMessage(id));
        }

        expert.Deactivate();
        await experts.SaveChangesAsync(cancellationToken);

        return Result<ExpertDto>.Ok(await GetRequiredAsync(id, cancellationToken));
    }

    private Task DrainSpecialtiesAsync(Expert expert, CancellationToken cancellationToken) =>
        drainer.TryDrainAsync(expert.Specialties.Select(s => s.Subject).ToList(), cancellationToken);

    private async Task<ExpertDto> GetRequiredAsync(int id, CancellationToken cancellationToken) =>
        ExpertDto.From(await experts.GetAsync(id, cancellationToken)
            ?? throw new InvalidOperationException($"Expert {id} disappeared."));

    private static string NotFoundMessage(int id) => $"Expert {id} was not found.";
}
