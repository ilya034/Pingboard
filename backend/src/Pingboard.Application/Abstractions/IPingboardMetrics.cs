namespace Pingboard.Application.Abstractions;

/// <summary>
///     Порт записи бизнес-метрик и метрик выполнения сценариев приложения Pingboard.
/// </summary>
public interface IPingboardMetrics
{
    /// <summary>Фиксирует успешную регистрацию нового пользователя.</summary>
    void RecordUserRegistered();

    /// <summary>Фиксирует попытку входа пользователя (успешную или неуспешную).</summary>
    void RecordLoginAttempt(bool success);

    /// <summary>Фиксирует создание нового монитора.</summary>
    void RecordMonitorCreated();

    /// <summary>Фиксирует удаление монитора.</summary>
    void RecordMonitorDeleted();

    /// <summary>Фиксирует результат и длительность проверки доступности целевого URL.</summary>
    void RecordProbeCheck(bool up, double durationSeconds);

    /// <summary>Фиксирует завершение итерации фонового воркера проверок.</summary>
    void RecordWorkerIteration(bool success);
}
