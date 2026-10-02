using Pingboard.Application.Monitors.Dtos;
using Pingboard.Application.Monitors.UseCases;

namespace Pingboard.Api.Endpoints;

/// <summary>
///     Endpoints тонкие: разобрали вход → вызвали сценарий → вернули DTO/статус.
///     Вся группа требует access-токен: владелец монитора берётся из claim sub,
///     «анонимного» владельца здесь быть не может.
/// </summary>
public static class MonitorEndpoints
{
    public static IEndpointRouteBuilder MapMonitorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/monitors")
            .WithTags("monitors")
            .RequireAuthorization();
        
        group.MapGet("", async (ListMonitors useCase, CancellationToken ct) =>
                Results.Ok(await useCase.ExecuteAsync(ct)))
            .WithName("ListMonitors")
            .WithSummary("Monitors list");

        group.MapPost("", async (CreateMonitorRequest request, CreateMonitor useCase, CancellationToken ct) =>
            {
                var created = await useCase.ExecuteAsync(request, ct);
                return Results.Created($"/api/monitors/{created.Id}", created);
            })
            .WithName("CreateMonitor")
            .WithSummary("Create a new monitor");

        group.MapGet("/{id:guid}", async (Guid id, GetMonitor useCase, CancellationToken ct) =>
                Results.Ok(await useCase.ExecuteAsync(id, ct)))
            .WithName("GetMonitor")
            .WithSummary("Get monitor details");

        group.MapPatch("/{id:guid}",
                async (Guid id, UpdateMonitorRequest request, UpdateMonitor useCase, CancellationToken ct) =>
                    Results.Ok(await useCase.ExecuteAsync(id, request, ct)))
            .WithName("UpdateMonitor")
            .WithSummary("Partially update a monitor (name/url/interval/enabled)");

        group.MapDelete("/{id:guid}", async (Guid id, DeleteMonitor useCase, CancellationToken ct) =>
            {
                await useCase.ExecuteAsync(id, ct);
                return Results.NoContent();
            })
            .WithName("DeleteMonitor")
            .WithSummary("Delete a monitor along with its check history");

        group.MapGet("/{id:guid}/checks", async (
                    Guid id,
                    GetMonitorChecks useCase,
                    CancellationToken ct,
                    DateTimeOffset? from = null,
                    DateTimeOffset? to = null,
                    int? limit = null) =>
                Results.Ok(await useCase.ExecuteAsync(id, from, to, limit, ct)))
            .WithName("GetMonitorChecks")
            .WithSummary("History of monitor checks for a time window");

        return app;
    }
}
