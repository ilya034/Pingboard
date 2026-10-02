using Pingboard.Application.Abstractions;
using Pingboard.Application.Common;
using Pingboard.Application.Monitors.Dtos;
using Pingboard.Application.Validation;

namespace Pingboard.Application.Monitors.UseCases;

/// <summary>История проверок монитора за окно с ограничением на количество записей.</summary>
public sealed class GetMonitorChecks(
    IMonitorRepository monitors,
    ICheckRepository checks,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    public const int DefaultLimit = 500;
    public const int MaxLimit = 5_000;

    public async Task<CheckPageDto> ExecuteAsync(Guid id, DateTimeOffset? from, DateTimeOffset? to, int? limit,
        CancellationToken ct)
    {
        var monitor = await OwnedMonitor.LoadAsync(monitors, id, currentUser, ct);

        var now = timeProvider.GetUtcNow();
        var windowTo = to ?? now;
        var windowFrom = from ?? windowTo - TimeSpan.FromHours(24);

        ValidateWindow(windowFrom, windowTo, limit);

        var take = limit ?? DefaultLimit;

        // Запрашиваем на одну запись больше запрошенного: если пришло take + 1, значит
        // в окне есть ещё — это и есть HasMore, без отдельного COUNT по всей истории.
        var history = await checks.HistoryAsync(monitor.Id, windowFrom, windowTo, take + 1, ct);
        var hasMore = history.Count > take;

        var items = history.Take(take).Select(MonitorMapper.ToDto).ToArray();

        return new CheckPageDto(monitor.Id, windowFrom, windowTo, hasMore, items);
    }

    /// <summary>
    ///     Границы окна и лимит — это вход клиента, и раньше «limit=0» молча превращался
    ///     в «limit=1», а перевёрнутое окно — в пустой ответ. Ошибка входа должна быть 400,
    ///     иначе клиент не понимает, почему данные не те.
    /// </summary>
    private static void ValidateWindow(DateTimeOffset from, DateTimeOffset to, int? limit)
    {
        var errors = new ValidationResult();

        if (limit is < 1 or > MaxLimit)
            errors.Add("limit", $"limit must be in the range 1..{MaxLimit}.");

        if (from > to)
            errors.Add("from", "from cannot be later than to.");

        if (!errors.IsValid) throw new ValidationFailedException(errors.Errors);
    }
}
