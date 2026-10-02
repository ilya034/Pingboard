namespace Pingboard.Application.Common;

/// <summary>
///     Логин/пароль не совпали — это именно 401 (клиент не аутентифицирован),
///     а не 403 (аутентифицирован, но нет прав). Ответ одинаков для «нет такого email»
///     и «неверный пароль», чтобы не подсказывать существование аккаунта.
/// </summary>
public sealed class InvalidCredentialsException(string message) : UnauthorizedException(message);
