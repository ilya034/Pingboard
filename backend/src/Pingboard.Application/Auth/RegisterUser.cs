using Microsoft.Extensions.Logging;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Auth.Dtos;
using Pingboard.Application.Common;
using Pingboard.Application.Validation;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Auth;

/// <summary>
///     Регистрация пользователя: создать аккаунт и сразу выдать access-токен.
///     Владельцем запросов пользователь становится через claim sub этого токена —
///     никакого «пользователя по умолчанию» в сценарии нет.
/// </summary>
public sealed class RegisterUser(
    IUserRepository users,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<RegisterUser> logger)
{
    public async Task<AuthTokenDto> ExecuteAsync(RegisterRequest request, CancellationToken ct)
    {
        ValidationRunner.Run(new RegisterRequestValidator(), request);

        var email = User.NormalizeEmail(request.Email);

        var existing = await users.GetByEmailAsync(email, ct);
        if (existing is not null)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                [nameof(request.Email)] = ["Пользователь с таким email уже зарегистрирован."]
            });

        var user = User.Register(email, passwordHasher.Hash(request.Password!), timeProvider.GetUtcNow());

        await users.AddAsync(user, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Зарегистрирован пользователь {UserId}", user.Id);

        // Срок жизни токена берётся у выдачи: сценарий не угадывает его по своим часам.
        // Гонка двух регистраций на один email ловится на UNIQUE-индексе ux_users_email
        // и транслируется в 400 по полю email (ExceptionStatusMapper).
        var token = tokenService.Issue(user.Id);

        return new AuthTokenDto(token.AccessToken, token.ExpiresAt);
    }
}
