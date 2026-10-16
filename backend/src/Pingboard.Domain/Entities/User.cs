using Pingboard.Domain.Common;

namespace Pingboard.Domain.Entities;

/// <summary>
///     Пользователь системы (владелец мониторов).
/// </summary>
public sealed class User : Entity<Guid>
{
    public const int EmailMaxLength = 320;

    private User()
    {
    } // EF Core

    private User(Guid id, string email, string passwordHash, DateTimeOffset createdAt)
    {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
    }

    public string Email { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    ///     Регистрирует нового пользователя с предварительно вычисленным хешем пароля.
    /// </summary>
    /// <param name="email">Email пользователя.</param>
    /// <param name="passwordHash">Хеш пароля.</param>
    /// <param name="createdAt">Временная метка регистрации.</param>
    public static User Register(string email, string passwordHash, DateTimeOffset createdAt)
    {
        return new User(Guid.NewGuid(), NormalizeEmail(email), ValidateHash(passwordHash), createdAt);
    }

    /// <summary>
    ///     Нормализует адрес электронной почты к нижнему регистру и проверяет его структуру.
    /// </summary>
    /// <param name="email">Входной адрес электронной почты.</param>
    /// <returns>Нормализованный адрес электронной почты.</returns>
    public static string NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) throw DomainValidationException.For(nameof(Email), "email is required");

        var trimmed = email.Trim().ToLowerInvariant();

        // Проверка формата: запрещены внутренние пробелы, множественные символы '@' и некорректная структура домена.
        if (trimmed.Length > EmailMaxLength ||
            trimmed.Any(char.IsWhiteSpace) ||
            trimmed.Count(c => c == '@') != 1) throw DomainValidationException.For(nameof(Email), "email is invalid");

        var at = trimmed.IndexOf('@');
        var local = trimmed[..at];
        var domain = trimmed[(at + 1)..];

        if (local.Length == 0 || domain.Length == 0 ||
            domain.StartsWith('.') || domain.EndsWith('.') ||
            domain.Contains("..", StringComparison.Ordinal) ||
            !domain.Contains('.', StringComparison.Ordinal))
            throw DomainValidationException.For(nameof(Email), "email is invalid");

        return trimmed;
    }

    private static string ValidateHash(string? passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw DomainValidationException.For(nameof(PasswordHash), "password hash is required");

        return passwordHash;
    }
}
