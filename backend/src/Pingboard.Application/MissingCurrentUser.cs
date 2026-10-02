using Pingboard.Application.Abstractions;

namespace Pingboard.Application.Composition;

/// <summary>
///     Заглушка на случай, когда сценарии мониторов зарегистрированы, но реализации
///     «кто текущий пользователь» в процессе нет (например, worker: он не обслуживает запросы).
///     Падать при обращении — правильно: это ошибка конфигурации, а не рабочий режим.
/// </summary>
internal sealed class MissingCurrentUser : ICurrentUser
{
    public Guid UserId => throw new InvalidOperationException(
        "ICurrentUser не сконфигурирован: в Api его регистрирует JwtUserIdProvider (claim sub), " +
        "в worker этот порт не нужен, потому что сценарии мониторов там не выполняются.");
}