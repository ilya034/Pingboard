using Pingboard.Application.Abstractions;

namespace Pingboard.Application.Tests.Fakes;

public sealed class FakeCurrentUser(Guid userId) : ICurrentUser
{
    public Guid UserId { get; } = userId;
}
