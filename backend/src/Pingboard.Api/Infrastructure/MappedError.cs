namespace Pingboard.Api.Infrastructure;

/// <summary>
///     Решение о HTTP-ответе на исключение: статус, заголовок, детали и разбивка ошибок по полям.
///     Отдельный тип, а не кортеж из пяти элементов: на вызывающей стороне должно быть
///     очевидно, что где лежит, — иначе легко перепутать Detail и Title.
/// </summary>
/// <param name="Status">Код ответа.</param>
/// <param name="Title">Заголовок ProblemDetails (что случилось, в общем виде).</param>
/// <param name="Detail">Пояснение для клиента; для 5xx — общая формулировка без внутренностей.</param>
/// <param name="WwwAuthenticate">Значение заголовка WWW-Authenticate, если он нужен (401).</param>
/// <param name="Errors">Разбивка по полям (400): поле → сообщения.</param>
internal sealed record MappedError(
    int Status,
    string Title,
    string? Detail,
    string? WwwAuthenticate = null,
    IReadOnlyDictionary<string, string[]>? Errors = null);
