using Microsoft.Extensions.Logging.Abstractions;
using Pingboard.Application.Auth;
using Pingboard.Application.Auth.Dtos;
using Pingboard.Application.Common;
using Pingboard.Application.Tests.Fakes;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Tests.Auth;

/// <summary>
///     Вход: не подсказывать существование email и отдавать настоящий срок жизни токена.
///     Обе вещи ломаются незаметно — ответ остаётся 200/401, а утечка и баг контракта
///     видны только по времени ответа и по полю expiresAt.
/// </summary>
public sealed class LoginUserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Unknown_email_still_runs_password_verification()
    {
        var hasher = new FakePasswordHasher();
        var scenario = new LoginUser(
            new FakeUserRepository(),
            hasher,
            new FakeTokenService(Now.AddMinutes(120)),
            NullLogger<LoginUser>.Instance);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() =>
            scenario.ExecuteAsync(new LoginRequest("nobody@example.com", "guess-guess"), CancellationToken.None));

        // Без проверки по хешу-заглушке ветка «нет пользователя» возвращалась бы заметно
        // быстрее, и по времени ответа можно было бы перебрать существующие адреса.
        Assert.Equal(1, hasher.VerifyCalls);
        Assert.Equal(FakePasswordHasher.Dummy, hasher.LastVerifiedHash);
    }

    [Fact]
    public async Task Successful_login_returns_the_expiry_from_the_token_issuer()
    {
        var users = new FakeUserRepository();
        var user = User.Register("demo@pingboard.local", "demo-password", Now);
        await users.AddAsync(user, CancellationToken.None);

        var expiresAt = Now.AddMinutes(120);
        var tokens = new FakeTokenService(expiresAt);
        var scenario = new LoginUser(users, new FakePasswordHasher(), tokens, NullLogger<LoginUser>.Instance);

        var dto = await scenario.ExecuteAsync(
            new LoginRequest("  Demo@Pingboard.LOCAL ", "demo-password"),
            CancellationToken.None);

        // expiresAt — момент истечения токена. Раньше сюда попадало «сейчас», и фронт,
        // который по этому полю решает, когда обновлять токен, считал бы его вечно протухшим.
        Assert.Equal(expiresAt, dto.ExpiresAt);
        Assert.Equal($"token-for-{user.Id}", dto.AccessToken);
        Assert.Equal(user.Id, tokens.IssuedFor);
    }
}
