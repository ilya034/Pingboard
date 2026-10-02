namespace Pingboard.Worker.Hosting.Logging;

/// <summary>
///     Scope'ы логирования живут в AsyncLocal как иммутабельный связный список: у каждой
///     async-ветки своя голова списка, поэтому параллельные проверки не могут ни перемешать
///     свои scope'ы, ни «съесть» чужой при Dispose.
///     Мутируемый <c>Stack&lt;object&gt;</c> в AsyncLocal этого не даёт: дочерние контексты
///     наследуют ссылку на один и тот же экземпляр, push/pop идут в общий стек, а сам
///     Stack не потокобезопасен.
/// </summary>
internal static class ScopeStack
{
    private static readonly AsyncLocal<ScopeNode?> Head = new();

    /// <summary>Текущие scope'ы от внешнего к внутреннему (как в штатном JSON-форматтере).</summary>
    public static IReadOnlyList<object> Current
    {
        get
        {
            var states = new List<object>();

            for (var node = Head.Value; node is not null; node = node.Parent)
                states.Add(node.State);

            states.Reverse();

            return states;
        }
    }

    /// <summary>Добавляет scope и возвращает голову, которая была до него (её восстановит Scope.Dispose).</summary>
    public static ScopeNode? Push(object state)
    {
        var parent = Head.Value;
        Head.Value = new ScopeNode(state, parent);

        return parent;
    }

    /// <summary>Возвращает голову списка в состояние до входа в scope.</summary>
    public static void SetCurrent(ScopeNode? node)
    {
        Head.Value = node;
    }
}
