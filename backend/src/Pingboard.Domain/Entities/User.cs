using Pingboard.Domain.Common;

namespace Pingboard.Domain.Entities;

/// <summary>
///     Владелец мониторов.
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
    ///     Фабрика принимает уже посчитанный хеш: домен не знает ни про BCrypt, ни про PBKDF2 —
    ///     это порт <c>IPasswordHasher</c> в Application.
    /// </summary>
    public static User Register(string email, string passwordHash, DateTimeOffset createdAt)
    {
        return new User(Guid.NewGuid(), NormalizeEmail(email), ValidateHash(passwordHash), createdAt);
    }

    /// <summary>Email нормализуется к нижнему регистру — иначе UNIQUE не спасёт от дублей вида A@b / a@B.</summary>
    public static string NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) throw DomainValidationException.For(nameof(Email), "email is required");

        var trimmed = email.Trim().ToLowerInvariant();

        // Проверка намеренно строже, чем «есть собака и она не с краю»: адрес с пробелом внутри,
        // с двумя собаками или с доменом без точки — это не «странный, но рабочий» email,
        // а заведомо недоставляемый адрес (и, например, «a@b» ломает представление о домене).
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
