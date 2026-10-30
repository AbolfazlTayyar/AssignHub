using AssignHub.Application.Abstractions;
using AssignHub.Domain.Requests;
using AssignHub.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AssignHub.Infrastructure.Stores;

internal sealed class RequestStore(AssignHubDbContext db) : IRequestStore
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public async Task<AddRequestResult> TryAddAsync(Request request, CancellationToken cancellationToken)
    {
        db.Requests.Add(request);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return AddRequestResult.Inserted(request);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException
        {
            Number: UniqueIndexViolation or UniqueConstraintViolation,
        })
        {
            // SPEC §6.2: UX_Requests_RequestKey decided the race; the winner's row is committed by now.
            db.Entry(request).State = EntityState.Detached;
            var existing = await db.Requests
                .AsNoTracking()
                .SingleAsync(r => r.RequestKey == request.RequestKey, cancellationToken);
            return AddRequestResult.Duplicate(existing);
        }
    }

    public Task<Request?> GetAsync(int id, CancellationToken cancellationToken) =>
        db.Requests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
}
