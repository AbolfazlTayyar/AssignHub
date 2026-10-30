using AssignHub.Application.Abstractions;
using AssignHub.Domain.Experts;
using AssignHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssignHub.Infrastructure.Stores;

internal sealed class ExpertStore(AssignHubDbContext db) : IExpertStore
{
    public async Task AddAsync(Expert expert, CancellationToken cancellationToken)
    {
        db.Experts.Add(expert);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<Expert?> GetAsync(int id, CancellationToken cancellationToken) =>
        db.Experts
            .AsNoTracking()
            .Include(e => e.Specialties)
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<Expert?> FindForUpdateAsync(int id, CancellationToken cancellationToken) =>
        db.Experts
            .Include(e => e.Specialties)
            .SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Expert>> ListAsync(CancellationToken cancellationToken) =>
        await db.Experts
            .AsNoTracking()
            .Include(e => e.Specialties)
            .OrderBy(e => e.Id)
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
