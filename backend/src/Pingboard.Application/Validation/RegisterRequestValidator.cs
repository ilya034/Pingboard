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
            result.Add(AuthValidationFields.Email, "Email is required.");
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
            result.Add(AuthValidationFields.Password, "Password is required.");
        else if (request.Password.Length < PasswordRules.MinLength)
            result.Add(AuthValidationFields.Password, $"Password must be at least {PasswordRules.MinLength} characters long.");
        else if (request.Password.Length > PasswordRules.MaxLength)
            // PBKDF2 by a huge password is still relatively cheap, but it is a DoS vector: trim before hashing.
            result.Add(AuthValidationFields.Password, $"Password must not exceed {PasswordRules.MaxLength} characters.");

        return result;
    }
}
