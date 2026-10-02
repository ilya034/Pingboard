namespace Pingboard.Worker.Hosting.Logging;

/// <summary>
///     Маркер scope'а логирования. При снятии восстанавливает ровно ту голову списка,
///     которая была до входа в scope (а не «снимает вершину», как делал бы общий стек).
/// </summary>
/// <param name="parent">Голова списка scope'ов до входа в этот scope.</param>
internal sealed class Scope(ScopeNode? parent) : IDisposable
{
    public void Dispose()
    {
        ScopeStack.SetCurrent(parent);
    }
}
