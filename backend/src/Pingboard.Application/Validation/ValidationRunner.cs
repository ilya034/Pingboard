using Pingboard.Application.Common;

namespace Pingboard.Application.Validation;

public static class ValidationRunner
{
    /// <summary>Бросает ValidationFailedException, если правила нарушены. Все ошибки собираются сразу.</summary>
    public static void Run<T>(IRequestValidator<T> validator, T request)
    {
        var result = validator.Validate(request);

        if (!result.IsValid) throw new ValidationFailedException(result.Errors);
    }
}
