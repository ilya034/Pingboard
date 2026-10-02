using Npgsql;
using Pingboard.Application.Auth.Dtos;
using Pingboard.Application.Common;
using Pingboard.Domain;

namespace Pingboard.Api.Infrastructure;

/// <summary>
///     Исключение слоя → HTTP-статус, заголовок и (при наличии) словарь ошибок по полям.
///     Единственное место, где принимается решение о коде ответа и для исключений
///     из сценариев, и для ответов схемы аутентификации.
/// </summary>
internal static class ExceptionStatusMapper
{
    /// <summary>Насколько глубоко искать первопричину: EF Core заворачивает NpgsqlException в своё.</summary>
    private const int MaxInnerDepth = 8;

    /// <summary>
    ///     Имя UNIQUE-индекса <c>users.email</c> из <c>UserConfiguration</c> (Infrastructure):
    ///     по нему отличаем гонку регистраций от прочих нарушений уникальности.
    ///     Совпадение с моделью EF Core проверяет тест <c>DatabaseContractTests</c>.
    /// </summary>
    internal const string UsersEmailIndex = "ux_users_email";

    public static MappedError Map(Exception? exception)
    {
        return exception switch
        {
            ValidationFailedException validation => new MappedError(
                StatusCodes.Status400BadRequest,
                "Request validation failed.",
                "One or more fields are invalid.",
                Errors: validation.Errors),

            // Ошибка входных данных домена. Поле берётся из отдельного свойства, поэтому
            // в разбивке оно одно: раньше ключом был «domain», а в сообщении дублировалось «Email: …».
            DomainValidationException domain => new MappedError(
                StatusCodes.Status400BadRequest,
                "Business rule violated.",
                domain.Message,
                Errors: new Dictionary<string, string[]> { [domain.Field ?? "domain"] = [domain.Message] }),

            // Minimal API бросает это при разборе тела запроса: битый JSON — ошибка клиента (400),
            // а не сбой сервиса. Иначе каждая опечатка клиента превращается в «алертный» 500.
            BadHttpRequestException bad => new MappedError(
                bad.StatusCode,
                "Request body could not be read.",
                "A valid JSON body matching the request schema is required.",
                Errors: new Dictionary<string, string[]> { ["body"] = ["Invalid request body."] }),

            // Гонка двух регистраций на один email: проверка GetByEmailAsync проходит у обоих,
            // и нарушение ux_users_email приходит уже из Postgres. Это 400 по полю email,
            // а не 503 «база не отвечает» и не 500: клиенту нужно то же поле, что и при обычной валидации.
            _ when HasUniqueViolation(exception, UsersEmailIndex) => new MappedError(
                StatusCodes.Status400BadRequest,
                "Request validation failed.",
                "A user with this email is already registered.",
                Errors: new Dictionary<string, string[]>
                {
                    [nameof(RegisterRequest.Email)] = ["A user with this email is already registered."]
                }),

            // Прочие нарушения уникальности — конфликт состояния, а не ошибка входных данных.
            _ when HasUniqueViolation(exception, null) => new MappedError(
                StatusCodes.Status409Conflict,
                "Unique constraint violated.",
                "An object with these values already exists."),

            // 401 — «кто ты?»: нет/просрочен/подделан токен, либо неверная пара email+пароль.
            // 403 — «тебе нельзя»: токен валиден, но ресурс чужой.
            UnauthorizedException unauthorized => new MappedError(
                StatusCodes.Status401Unauthorized,
                unauthorized.Message,
                unauthorized.Message,
                ErrorResponseFormat.BearerChallenge),

            NotFoundException notFound => new MappedError(
                StatusCodes.Status404NotFound,
                notFound.Message,
                notFound.Message),

            ForbiddenException forbidden => new MappedError(
                StatusCodes.Status403Forbidden,
                forbidden.Message,
                forbidden.Message),

            OperationCanceledException => new MappedError(
                StatusCodes.Status499ClientClosedRequest,
                "Request cancelled by client.",
                "Client disconnected before the response was sent."),

            // Недоступная БД — это не «внутренняя ошибка сервиса», а состояние backing service
            // (фактор IV). 503 честнее 500: он говорит «я жив, но зависимость недоступна»,
            // и согласуется с /readyz, который в этой ситуации тоже отвечает 503.
            _ when IsBackingServiceUnavailable(exception) => new MappedError(
                StatusCodes.Status503ServiceUnavailable,
                "Service temporarily unavailable: the database is unresponsive.",
                "Service temporarily unavailable, please try again later."),

            _ => new MappedError(
                StatusCodes.Status500InternalServerError,
                "Internal server error.",
                "Service temporarily unavailable, please try again later.")
        };
    }

    /// <summary>
    ///     Ошибка подключения к Postgres. EF Core заворачивает NpgsqlException в
    ///     InvalidOperationException («likely due to a transient failure»), поэтому смотрим
    ///     цепочку InnerException, а не только верхний тип.
    ///     <see cref="PostgresException" /> — наследник NpgsqlException, но это уже
    ///     <b>серверная</b> ошибка (синтаксис, ограничение, права), а не «БД не отвечает»:
    ///     503 заслуживают только транзиентные состояния (кончились соединения, сервер
    ///     поднимается/останавливается). Иначе любая SQL-ошибка выглядела бы как недоступная БД.
    /// </summary>
    private static bool IsBackingServiceUnavailable(Exception? exception)
    {
        for (var depth = 0; exception is not null && depth < MaxInnerDepth; depth++)
        {
            switch (exception)
            {
                case PostgresException postgres:
                    return postgres.IsTransient;
                case NpgsqlException:
                case TimeoutException:
                    return true;
            }

            exception = exception.InnerException;
        }

        return false;
    }

    /// <summary>
    ///     Нарушение уникальности (SQLSTATE 23505) в цепочке исключений EF Core.
    ///     <paramref name="constraintName" /> == null — подходит любое нарушение уникальности.
    /// </summary>
    private static bool HasUniqueViolation(Exception? exception, string? constraintName)
    {
        var postgres = FindPostgresError(exception);

        return postgres is { SqlState: PostgresErrorCodes.UniqueViolation } &&
               (constraintName is null || postgres.ConstraintName == constraintName);
    }

    private static PostgresException? FindPostgresError(Exception? exception)
    {
        for (var depth = 0; exception is not null && depth < MaxInnerDepth; depth++)
        {
            if (exception is PostgresException postgres) return postgres;

            exception = exception.InnerException;
        }

        return null;
    }
}
