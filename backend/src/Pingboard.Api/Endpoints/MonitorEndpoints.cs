using Pingboard.Application.Monitors.Dtos;
using Pingboard.Application.Monitors.UseCases;

namespace Pingboard.Api.Endpoints;

/// <summary>
///     Endpoints тонкие: разобрали вход → вызвали сценарий → вернули DTO/статус.
///     Никакой бизнес-логики и валидации здесь нет (§13 PLAN.md).
///     Вся группа требует access-токен: владелец монитора берётся из claim sub,
///     поэтому «анонимного» владельца здесь быть не может.
/// </summary>
public static class MonitorEndpoints
{
    public static IEndpointRouteBuilder MapMonitorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/monitors")
            .WithTags("monitors")
            .RequireAuthorization();

        // Шаблон именно пустой, а не "/": MapGroup("/api/monitors") + "/" даёт маршрут
        // /api/monitors/ со слэшем, а endpoint routing слэш не нормализует — запрос
        // к /api/monitors (как в Location от CreateMonitor) отвечал бы 404.
        group.MapGet("", async (ListMonitors useCase, CancellationToken ct) =>
                Results.Ok(await useCase.ExecuteAsync(ct)))
            .WithName("ListMonitors")
            .WithSummary("Список мониторов с текущим статусом и uptime24h");

        group.MapPost("", async (CreateMonitorRequest request, CreateMonitor useCase, CancellationToken ct) =>
            {
                var created = await useCase.ExecuteAsync(request, ct);
                return Results.Created($"/api/monitors/{created.Id}", created);
            })
            .WithName("CreateMonitor")
            .WithSummary("Создать монитор");

        group.MapGet("/{id:guid}", async (Guid id, GetMonitor useCase, CancellationToken ct) =>
                Results.Ok(await useCase.ExecuteAsync(id, ct)))
            .WithName("GetMonitor")
            .WithSummary("Детали монитора");

        group.MapPatch("/{id:guid}",
                async (Guid id, UpdateMonitorRequest request, UpdateMonitor useCase, CancellationToken ct) =>
                    Results.Ok(await useCase.ExecuteAsync(id, request, ct)))
            .WithName("UpdateMonitor")
            .WithSummary("Частичное обновление монитора (name/url/interval/enabled)");

        group.MapDelete("/{id:guid}", async (Guid id, DeleteMonitor useCase, CancellationToken ct) =>
            {
                await useCase.ExecuteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteMonitor")
            .WithSummary("Удалить монитор вместе с историей проверок");

        group.MapGet("/{id:guid}/checks", async (
                    Guid id,
                    GetMonitorChecks useCase,
                    CancellationToken ct,
                    DateTimeOffset? from = null,
                    DateTimeOffset? to = null,
                    int? limit = null) =>
                Results.Ok(await useCase.ExecuteAsync(id, from, to, limit, ct)))
            .WithName("GetMonitorChecks")
            .WithSummary("История проверок монитора за окно");

        return app;
    }
}
