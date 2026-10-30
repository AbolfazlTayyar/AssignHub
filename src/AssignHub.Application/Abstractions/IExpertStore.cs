using AssignHub.Domain.Experts;

namespace AssignHub.Application.Abstractions;

public interface IExpertStore
{
    Task AddAsync(Expert expert, CancellationToken cancellationToken);

    // Tracked, so Activate/Deactivate followed by SaveChangesAsync writes only IsActive (SPEC §4.5).
    Task<Expert?> GetAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Expert>> ListAsync(CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
