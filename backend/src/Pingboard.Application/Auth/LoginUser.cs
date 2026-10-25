using Microsoft.Extensions.Logging;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Auth.Dtos;
using Pingboard.Application.Common;
using Pingboard.Application.Observability;
using Pingboard.Application.Validation;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Auth;

/// <summary>
///     Сценарий аутентификации пользователя: проверяет учётные данные и выпускает access-токен.
/// </summary>
public sealed class LoginUser(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    ILogger<LoginUser> logger,
    IPingboardMetrics? metrics = null)
{
    public async Task<AuthTokenDto> ExecuteAsync(LoginRequest request, CancellationToken ct)
    {
        ValidationRunner.Run(new LoginRequestValidator(), request);

        var email = User.NormalizeEmail(request.Email);
        var user = await users.GetByEmailAsync(email, ct);

        // Постоянное время ответа: проверка пароля выполняется всегда, даже если пользователь
        // не найден (используется фиктивный хеш аналогичной сложности для предотвращения timing attacks).
        var passwordMatches = passwordHasher.Verify(request.Password!, user?.PasswordHash ?? passwordHasher.DummyHash);

        var telemetry = metrics ?? PingboardMetrics.Default;

        if (user is null || !passwordMatches)
        {
            telemetry.RecordLoginAttempt(success: false);

            // В логе нет персональных данных (email и пароля).
            logger.LogWarning("Failed login attempt");
            throw new InvalidCredentialsException("Invalid email or password.");
        }

        telemetry.RecordLoginAttempt(success: true);

        var token = tokenService.Issue(user.Id);

        return new AuthTokenDto(token.AccessToken, token.ExpiresAt);
    }
}
