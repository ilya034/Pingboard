namespace Pingboard.Application.Monitors.Dtos;

/// <summary>
///     Ограниченный список проверок. <paramref name="HasMore" /> говорит, что в окне есть
///     ещё записи за пределами <paramref name="Items" />: без этого клиент не может отличить
///     «история кончилась» от «её обрезал limit».
/// </summary>
public sealed record CheckPageDto(
    Guid MonitorId,
    DateTimeOffset From,
    DateTimeOffset To,
    bool HasMore,
    IReadOnlyList<MonitorCheckDto> Items);
