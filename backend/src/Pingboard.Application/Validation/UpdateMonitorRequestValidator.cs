using Pingboard.Application.Monitors.Dtos;

namespace Pingboard.Application.Validation;

/// <summary>Валидатор PATCH-запроса: обязательных полей нет, но переданные должны быть корректны.</summary>
public sealed class UpdateMonitorRequestValidator : IRequestValidator<UpdateMonitorRequest>
{
    public ValidationResult Validate(UpdateMonitorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = new ValidationResult();

        MonitorValidationRules.CheckName(result, request.Name, false);
        MonitorValidationRules.CheckUrl(result, request.Url, false);
        MonitorValidationRules.CheckInterval(result, request.IntervalSeconds);

        return result;
    }
}
