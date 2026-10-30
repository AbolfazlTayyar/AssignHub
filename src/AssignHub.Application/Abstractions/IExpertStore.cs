using AssignHub.Domain.Experts;

namespace AssignHub.Application.Abstractions;

public interface IExpertStore
{
    Task AddAsync(Expert expert, CancellationToken cancellationToken);

    // Not tracked, so it always reflects the latest OpenCount after a drain.
    Task<Expert?> GetAsync(int id, CancellationToken cancellationToken);

    // Tracked, so Activate/Deactivate followed by SaveChangesAsync writes only IsActive (SPEC §4.5).
    Task<Expert?> FindForUpdateAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Expert>> ListAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
