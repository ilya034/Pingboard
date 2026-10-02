using Pingboard.Application.Auth.Dtos;

namespace Pingboard.Application.Validation;

/// <summary>
///     Валидатор входа: обе части обязательны, содержимое проверяется аутентификацией.
///     Длина пароля ограничена и здесь: на входе выполняется тот же PBKDF2, что и при регистрации.
///     Ключи ошибок — имена полей JSON (email/password), как и у валидаторов монитора
///     (name/url/intervalSeconds): один и тот же клиент парсит один и тот же формат.
/// </summary>
public sealed class LoginRequestValidator : IRequestValidator<LoginRequest>
{
    public ValidationResult Validate(LoginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(request.Email))
            result.Add(AuthValidationFields.Email, "Email обязателен.");

        if (string.IsNullOrWhiteSpace(request.Password))
            result.Add(AuthValidationFields.Password, "Пароль обязателен.");
        else if (request.Password.Length > PasswordRules.MaxLength)
            result.Add(AuthValidationFields.Password, $"Пароль не длиннее {PasswordRules.MaxLength} символов.");

        return result;
    }
}
