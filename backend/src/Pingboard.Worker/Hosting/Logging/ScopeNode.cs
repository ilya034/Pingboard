namespace Pingboard.Worker.Hosting.Logging;

/// <summary>
///     Узел иммутабельного списка scope'ов: хранит своё состояние и ссылку на внешний scope.
///     Ничего не мутируется — именно поэтому параллельные ветки не видят scope'ы друг друга.
/// </summary>
/// <param name="State">Объект scope'а, как его передал вызывающий.</param>
/// <param name="Parent">Внешний scope (null — этот scope самый внешний).</param>
internal sealed class ScopeNode(object state, ScopeNode? parent)
{
    public object State { get; } = state;

    public ScopeNode? Parent { get; } = parent;
}
