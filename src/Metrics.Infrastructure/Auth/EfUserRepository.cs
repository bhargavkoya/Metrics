using Metrics.Application.Auth;
using Metrics.Application.Common;
using Metrics.Domain.Entities;
using Metrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Metrics.Infrastructure.Auth;

public class EfUserRepository(MetricsDbContext db) : IUserRepository
{
    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

    public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task AddAsync(User user, CancellationToken ct)
    {
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent registration with the same email loses the race on the unique index.
            db.Entry(user).State = EntityState.Detached;
            if (await db.Users.AnyAsync(u => u.Email == user.Email, ct))
                throw new ConflictException("An account with this email already exists.");
            throw;
        }
    }
}
