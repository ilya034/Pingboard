namespace Pingboard.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);

    /// <summary>
    ///     Хеш-заглушка того же формата и стоимости, что и настоящие. Нужна, чтобы проверка
    ///     пароля при отсутствующем пользователе стоила столько же, сколько при существующем:
    ///     иначе по времени ответа видно, зарегистрирован ли email (user enumeration).
    /// </summary>
    string DummyHash { get; }
}
