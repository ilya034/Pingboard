namespace Pingboard.Application.Validation;

/// <summary>Копилка ошибок валидации: поле → сообщения.</summary>
public sealed class ValidationResult
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public bool IsValid => _errors.Count == 0;

    public IReadOnlyDictionary<string, string[]> Errors =>
        _errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(), StringComparer.Ordinal);

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            list = [];
            _errors[field] = list;
        }

        list.Add(message);
    }
}
