namespace Pingboard.Application.Abstractions;

/// <summary>Выдача access-токена (JWT — деталь Infrastructure).</summary>
public interface ITokenService
{
    /// <summary>
    ///     Выдать токен пользователю. Возвращается и токен, и срок его жизни: сценарий
    ///     не вычисляет <c>expiresAt</c> сам, иначе клиент получает «сейчас» вместо истечения.
    /// </summary>
    IssuedToken Issue(Guid userId);
}
