using Pingboard.Application.Abstractions;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Tests.Fakes;

public sealed class FakeUserRepository : IUserRepository
{
    private readonly List<User> _users = [];

    public Task AddAsync(User user, CancellationToken ct)
    {
        _users.Add(user);
        return Task.CompletedTask;
    }

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct)
    {
        return Task.FromResult(_users.FirstOrDefault(u => u.Email == normalizedEmail));
    }

    public Task<User?> GetAsync(Guid id, CancellationToken ct)
    {
        return Task.FromResult(_users.FirstOrDefault(u => u.Id == id));
    }
}
