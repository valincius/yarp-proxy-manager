using Microsoft.EntityFrameworkCore;
using ProxyManager.Application.Streams;
using ProxyManager.Domain;

namespace ProxyManager.Infrastructure.Persistence;

public sealed class StreamRepository(ProxyDbContext db) : IStreamRepository
{
    public async Task<IReadOnlyList<Domain.Stream>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Streams.AsNoTracking().OrderBy(s => s.Name).ToListAsync(cancellationToken);

    public async Task<Domain.Stream?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.Streams.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task AddAsync(Domain.Stream stream, CancellationToken cancellationToken = default)
    {
        db.Streams.Add(stream);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Domain.Stream stream, CancellationToken cancellationToken = default)
    {
        db.Streams.Update(stream);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Domain.Stream stream, CancellationToken cancellationToken = default)
    {
        db.Streams.Remove(stream);
        await db.SaveChangesAsync(cancellationToken);
    }
}
