namespace Pingboard.Application.Validation;

/// <summary>
///     Имена полей auth-запросов в ошибках валидации. Совпадают с именами в JSON (camelCase),
///     как и ключи валидаторов монитора (name/url/intervalSeconds): клиент парсит один формат
///     независимо от того, какой сценарий вернул 400.
/// </summary>
internal static class AuthValidationFields
{
    public const string Email = "email";

    public const string Password = "password";
}
