namespace Pingboard.Application.Common;

/// <summary>
///     Ошибка входных данных сценария (транслируется в ProblemDetails 400).
///     Валидация живёт в Application, а не в endpoints.
/// </summary>
public sealed class ValidationFailedException : Exception
{
    public ValidationFailedException(IReadOnlyDictionary<string, string[]> errors)
        : base("Request validation failed.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
