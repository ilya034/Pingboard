namespace Pingboard.Application.Abstractions;

/// <summary>
///     Выданный access-токен и момент его истечения (UTC).
///     Срок жизни задаёт реализация порта, а не сценарий: иначе выдача и проверка
///     расходятся, и поле <c>expiresAt</c> в ответе перестаёт что-либо значить.
/// </summary>
/// <param name="AccessToken">Сам токен для заголовка <c>Authorization: Bearer …</c>.</param>
/// <param name="ExpiresAt">Момент (UTC), после которого токен не принимается.</param>
public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);
