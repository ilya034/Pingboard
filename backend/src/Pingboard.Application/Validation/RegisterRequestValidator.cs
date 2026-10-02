using Pingboard.Application.Auth.Dtos;
using Pingboard.Domain;
using Pingboard.Domain.Entities;

namespace Pingboard.Application.Validation;

/// <summary>
///     Валидатор регистрации: email нормализуем, пароль проверяем на длину.
///     Ограничения пароля — в <see cref="PasswordRules" />: они общие с входом.
/// </summary>
public sealed class RegisterRequestValidator : IRequestValidator<RegisterRequest>
{
    public ValidationResult Validate(RegisterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(request.Email))
            result.Add(AuthValidationFields.Email, "Email обязателен.");
        else
            try
            {
                User.NormalizeEmail(request.Email);
            }
            catch (DomainValidationException ex)
            {
                result.Add(AuthValidationFields.Email, ex.Message);
            }

        if (string.IsNullOrWhiteSpace(request.Password))
            result.Add(AuthValidationFields.Password, "Пароль обязателен.");
        else if (request.Password.Length < PasswordRules.MinLength)
            result.Add(AuthValidationFields.Password, $"Пароль не короче {PasswordRules.MinLength} символов.");
        else if (request.Password.Length > PasswordRules.MaxLength)
            // PBKDF2 по мегабайтному паролю — недорогая, но DoS-ручка: режем до хеширования.
            result.Add(AuthValidationFields.Password, $"Пароль не длиннее {PasswordRules.MaxLength} символов.");

        return result;
    }
}
