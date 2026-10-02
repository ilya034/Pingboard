namespace Pingboard.Domain;

/// <summary>Сущность не найдена (в Application транслируется в 404).</summary>
public sealed class NotFoundException : DomainException
{
    public NotFoundException(string message) : base(message)
    {
    }

    public static NotFoundException For<T>(object id)
    {
        return new NotFoundException($"{typeof(T).Name} '{id}' не найден");
    }
}
