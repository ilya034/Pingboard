namespace Pingboard.Application.Tests.Fakes;

/// <summary>Управляемое время вместо реальных часов.</summary>
public sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = now;

    public override DateTimeOffset GetUtcNow()
    {
        return Now;
    }

    public void Advance(TimeSpan delta)
    {
        Now = Now.Add(delta);
    }

    public void Set(DateTimeOffset value)
    {
        Now = value;
    }
}
