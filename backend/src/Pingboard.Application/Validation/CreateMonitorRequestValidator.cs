using Pingboard.Application.Monitors.Dtos;
using Pingboard.Domain.Common;

namespace Pingboard.Application.Validation;

/// <summary>Валидатор создания монитора: имя и URL обязательны.</summary>
public sealed class CreateMonitorRequestValidator : IRequestValidator<CreateMonitorRequest>
{
    public ValidationResult Validate(CreateMonitorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = new ValidationResult();

        MonitorValidationRules.CheckName(result, request.Name, true);
        MonitorValidationRules.CheckUrl(result, request.Url, true);
        MonitorValidationRules.CheckInterval(result, request.IntervalSeconds ?? MonitorRules.IntervalSecondsDefault);

        return result;
    }
}
