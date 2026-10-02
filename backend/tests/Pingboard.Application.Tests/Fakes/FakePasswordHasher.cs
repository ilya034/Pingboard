using Pingboard.Application.Abstractions;

namespace Pingboard.Application.Tests.Fakes;

/// <summary>
///     Фейк хешера паролей. «Хеш» здесь равен паролю: тесту важны не свойства PBKDF2,
///     а то, что проверка вообще выполняется — в том числе когда пользователя нет.
/// </summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public const string Dummy = "$dummy-hash$";

    public int VerifyCalls { get; private set; }

    public string? LastVerifiedHash { get; private set; }

    public string DummyHash => Dummy;

    public string Hash(string password)
    {
        return password;
    }

    public bool Verify(string password, string hash)
    {
        VerifyCalls++;
        LastVerifiedHash = hash;

        return password == hash;
    }
}
