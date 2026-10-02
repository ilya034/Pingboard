using Microsoft.Extensions.Logging;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Auth.Dtos;
using Pingboard.Application.Common;
using Pingboard.Application.Validation;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Auth;

/// <summary>Вход: проверяем пароль по хешу и выдаём access-токен.</summary>
public sealed class LoginUser(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    ILogger<LoginUser> logger)
{
    public async Task<AuthTokenDto> ExecuteAsync(LoginRequest request, CancellationToken ct)
    {
        ValidationRunner.Run(new LoginRequestValidator(), request);

        var email = User.NormalizeEmail(request.Email);
        var user = await users.GetByEmailAsync(email, ct);

        // Пароль проверяется ВСЕГДА, даже когда пользователя нет: для отсутствующего
        // берётся хеш-заглушка той же стоимости. Иначе ответ без пользователя возвращался бы
        // заметно быстрее, и по времени можно было бы перебрать существующие email.
        var passwordMatches = passwordHasher.Verify(request.Password!, user?.PasswordHash ?? passwordHasher.DummyHash);

        // Одинаковый ответ на «нет пользователя» и «неверный пароль» — не подсказываем, что email существует.
        // 401, а не 403: клиент ещё не аутентифицирован, и по этому коду фронт показывает форму входа,
        // а не «доступ запрещён».
        if (user is null || !passwordMatches)
        {
            // В логе нет ни email, ни пароля: это PII, а логи уходят в stdout и собираются платформой (фактор XI).
            logger.LogWarning("Failed login attempt");
            throw new InvalidCredentialsException("Invalid email or password.");
        }

        // Срок жизни токена берётся у выдачи: сценарий не угадывает его по своим часам.
        var token = tokenService.Issue(user.Id);

        return new AuthTokenDto(token.AccessToken, token.ExpiresAt);
    }
}
