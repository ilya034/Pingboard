using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Pingboard.Api.Infrastructure;

/// <summary>
///     Формат ответов об ошибке — один на весь API: тело RFC 7807 (<c>application/problem+json</c>)
///     и заголовок челленджа для 401. Держим в одном месте, потому что 401 отдают трое:
///     обработчик исключений (<see cref="ErrorHandling" />), схема JwtBearer
///     (<see cref="JwtBearerViewEvents" />) и rate limiter (<see cref="AuthRateLimiting" />).
/// </summary>
internal static class ErrorResponseFormat
{
    /// <summary>
    ///     RFC 7807: тело ошибки — <c>application/problem+json</c>. Ровно так его отдаёт
    ///     и сам ASP.NET Core из ProblemDetails-результатов.
    /// </summary>
    public const string ContentType = "application/problem+json";

    /// <summary>
    ///     RFC 6750: параметр realm обязателен. Голое «Bearer» часть клиентов
    ///     (.NET HttpWebRequest) считает невалидным челленджем и обрывает соединение.
    /// </summary>
    public const string BearerChallenge = "Bearer realm=\"pingboard\"";

    /// <summary>
    ///     Записать ProblemDetails с правильным media type.
    ///     Через <see cref="Results.Json{TValue}" />, а не <c>WriteAsJsonAsync</c>: у последнего
    ///     нет публичной перегрузки с contentType, и он безусловно ставит
    ///     «application/json; charset=utf-8» — RFC 7807 при этом нарушается.
    /// </summary>
    public static Task WriteAsync(HttpContext http, ProblemDetails problem)
    {
        return Results.Json(problem, contentType: ContentType, statusCode: problem.Status).ExecuteAsync(http);
    }
}
