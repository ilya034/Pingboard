using Microsoft.EntityFrameworkCore;
using Pingboard.Application.Abstractions;
using Pingboard.Domain.Entities;
using Pingboard.Infrastructure.Persistence;

namespace Pingboard.Infrastructure.Repositories;

public sealed class UserRepository(UptimeDbContext db) : IUserRepository
{
    public async Task AddAsync(User user, CancellationToken ct)
    {
        await db.Users.AddAsync(user, ct);
    }

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct)
    {
        return db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);
    }

    public Task<User?> GetAsync(Guid id, CancellationToken ct)
    {
        return db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
    }
}
