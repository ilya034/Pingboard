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
        "ICurrentUser is not configured: Api registers it via JwtUserIdProvider (claim sub), " +
        "while worker does not need this port because monitor scenarios are not executed there.");
}