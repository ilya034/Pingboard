using System.Security.Cryptography;
using Pingboard.Application.Abstractions;

namespace Pingboard.Infrastructure.Security;

/// <summary>
///     Хеширование паролей средствами BCL (PBKDF2-HMAC-SHA256).
///     В PLAN.md стоит BCrypt.Net-Next; пакета нет в оффлайн-фиде этой машины, поэтому
///     каркас обходится встроенным PBKDF2 — порт IPasswordHasher от этого не меняется,
///     замена реализации = замена класса в AddInfrastructure.
///     Формат хеша совместим по духу с остальными: algorithm$iterations$salt$hash.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Algorithm = "pbkdf2-sha256";
    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>
    ///     Хеш-заглушка считается один раз на процесс: стоимость Verify по ней равна
    ///     стоимости Verify по настоящему хешу, а соль у неё своя и случайная.
    /// </summary>
    public Pbkdf2PasswordHasher()
    {
        DummyHash = Hash(Guid.NewGuid().ToString("N"));
    }

    public string DummyHash { get; }

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);

        return string.Join('$', Algorithm, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash)) return false;

        var parts = hash.Split('$');
        if (parts.Length != 4 || parts[0] != Algorithm || !int.TryParse(parts[1], out var iterations)) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual =
                Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

            // Сравнение за постоянное время — иначе утечка по таймингу.
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}