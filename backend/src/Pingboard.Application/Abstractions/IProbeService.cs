using Pingboard.Domain.Common;

namespace Pingboard.Application.Abstractions;

/// <summary>
///     HTTP-проверка URL. Воркер про HttpClient не знает.
/// </summary>
public interface IProbeService
{
    /// <summary>
    ///     Проверяет URL и возвращает результат.
    ///     КОНТРАКТ: исключений не бросает — любая неудача (DNS, отказ соединения, таймаут,
    ///     запрет SSRF-барьера) возвращается как <see cref="ProbeOutcome" /> с <c>Ok = false</c>.
    ///     Единственное исключение — <see cref="OperationCanceledException" />, когда отменён
    ///     переданный <paramref name="ct" />: это остановка приложения, а не «монитор лежит»,
    ///     и записывать её в историю проверок нельзя.
    ///     Реализации обязаны соблюдать контракт: цикл воркера считает всю итерацию неуспешной,
    ///     если проба бросит исключение.
    /// </summary>
    Task<ProbeOutcome> CheckAsync(string url, CancellationToken ct);
}
