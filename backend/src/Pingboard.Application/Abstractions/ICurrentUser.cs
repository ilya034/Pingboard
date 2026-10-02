namespace Pingboard.Application.Abstractions;

/// <summary>
///     Владелец текущего запроса. Единственный источник — проверенный access-токен:
///     в Api порт реализует <c>JwtUserIdProvider</c> (claim <c>sub</c>), запасного
///     «пользователя по умолчанию» нет. В воркере порт не используется.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }
}
