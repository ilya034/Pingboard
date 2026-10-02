namespace Pingboard.Application.Validation;

/// <summary>
///     Мини-реализация валидации на чистом C#.
///     В PLAN.md на этом месте стоит FluentValidation. Пакет недоступен в оффлайн-фиде
///     этой машины (см. Directory.Packages.props), поэтому каркас обходится своими
///     валидаторами с теми же правилами. Переход на FluentValidation — механический:
///     правила в MonitorValidators один-в-один ложатся на RuleFor(...), а сигнатура Validate()
///     совпадает с IValidator&lt;T&gt;.Validate.
/// </summary>
public interface IRequestValidator<in T>
{
    ValidationResult Validate(T request);
}
