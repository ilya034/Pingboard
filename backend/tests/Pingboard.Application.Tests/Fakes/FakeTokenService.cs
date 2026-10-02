using Pingboard.Application.Abstractions;

namespace Pingboard.Application.Tests.Fakes;

/// <summary>
///     Фейк выдачи токенов: возвращает «токен» и тот срок жизни, который задал тест, —
///     так проверяется, что сценарий не подставляет свои часы вместо срока из выдачи.
/// </summary>
public sealed class FakeTokenService(DateTimeOffset expiresAt) : ITokenService
{
    public Guid? IssuedFor { get; private set; }

    public IssuedToken Issue(Guid userId)
    {
        IssuedFor = userId;

        return new IssuedToken($"token-for-{userId}", expiresAt);
    }
}
