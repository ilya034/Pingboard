namespace Pingboard.Domain;

/// <summary>Инвариант домена нарушен: неверные аргументы фабрики или метода-мутатора.</summary>
public sealed class DomainValidationException : DomainException
{
    public DomainValidationException(string message) : base(message)
    {
    }

    private DomainValidationException(string field, string reason) : base(reason)
    {
        Field = field;
    }

    /// <summary>
    ///     Поле, к которому относится нарушение. Отдельное свойство, а не префикс в тексте
    ///     сообщения: иначе маппер кладёт «Email: некорректный email» под ключом «Email»,
    ///     и клиент видит поле дважды. Может быть null, если исключение создано без поля.
    /// </summary>
    public string? Field { get; }

    public static DomainValidationException For(string field, string reason)
    {
        return new DomainValidationException(field, reason);
    }
}
