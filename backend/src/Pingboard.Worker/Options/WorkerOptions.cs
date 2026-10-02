namespace Pingboard.Worker.Options;

/// <summary>
///     Настройки цикла воркера, не относящиеся к самим проверкам, — период тика.
///     Размер батча и параллелизм живут только в <c>DueCheckOptions</c> (та же секция Worker):
///     два класса с одинаковыми полями на одну секцию расходятся молча, и в логе оказывается
///     одно, а в работе — другое.
/// </summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>Пауза между итерациями цикла.</summary>
    public int TickSeconds { get; set; } = 5;

    /// <summary>
    ///     Файл-пульс: воркер обновляет его после каждой итерации, а healthcheck (compose)
    ///     проверяет свежесть. У воркера нет HTTP-порта, поэтому иначе «процесс жив» не отличить
    ///     от «процесс жив, а цикл встал» — зависший запрос к БД выглядит абсолютно здоровым.
    ///     Пустое значение отключает запись пульса.
    /// </summary>
    public string HeartbeatPath { get; set; } = Path.Combine(Path.GetTempPath(), "pingboard-worker-heartbeat");
}
