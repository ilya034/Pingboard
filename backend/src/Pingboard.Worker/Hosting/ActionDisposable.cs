namespace Pingboard.Worker.Hosting;

/// <summary>Обёртка «действие как IDisposable» для снятия подписок на сигналы остановки.</summary>
internal sealed class ActionDisposable(Action action) : IDisposable
{
    public void Dispose()
    {
        action();
    }
}
