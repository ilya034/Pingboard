namespace Pingboard.Application.Validation;

/// <summary>
///     Правила пароля, общие для регистрации и входа: на входе выполняется тот же PBKDF2
///     (210k итераций), поэтому мегабайтный пароль — это DoS-ручка на обоих маршрутах.
/// </summary>
public static class PasswordRules
{
    public const int MinLength = 8;

    /// <summary>Верхняя граница: защита от «пароля» в мегабайт, который считает PBKDF2.</summary>
    public const int MaxLength = 128;
}
