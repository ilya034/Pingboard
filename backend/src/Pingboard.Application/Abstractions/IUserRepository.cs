using Pingboard.Domain.Entities;

namespace Pingboard.Application.Abstractions;

public interface IUserRepository
{
    Task AddAsync(User user, CancellationToken ct);

    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct);

    Task<User?> GetAsync(Guid id, CancellationToken ct);
}