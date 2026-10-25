using Microsoft.Extensions.Logging;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Auth.Dtos;
using Pingboard.Application.Common;
using Pingboard.Application.Observability;
using Pingboard.Application.Validation;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Auth;

/// <summary>
///     Сценарий регистрации пользователя: создание учётной записи и выпуск первичного access-токена.
/// </summary>
public sealed class RegisterUser(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<RegisterUser> logger,
    IPingboardMetrics? metrics = null)
{
    public async Task<AuthTokenDto> ExecuteAsync(RegisterRequest request, CancellationToken ct)
    {
        ValidationRunner.Run(new RegisterRequestValidator(), request);

        var email = User.NormalizeEmail(request.Email);

        var existing = await users.GetByEmailAsync(email, ct);
        if (existing is not null)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                [nameof(request.Email)] = ["A user with this email is already registered."]
            });

        var user = User.Register(email, passwordHasher.Hash(request.Password!), timeProvider.GetUtcNow());

        await users.AddAsync(user, ct);
        await unitOfWork.SaveChangesAsync(ct);

        (metrics ?? PingboardMetrics.Default).RecordUserRegistered();
        logger.LogInformation("User registered {UserId}", user.Id);

        // Конфликты параллельной регистрации обрабатываются ограничением уникальности таблицы БД.
        var token = tokenService.Issue(user.Id);

        return new AuthTokenDto(token.AccessToken, token.ExpiresAt);
    }
}
