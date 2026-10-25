using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Auth;
using Pingboard.Application.Composition;
using Pingboard.Application.Monitors.UseCases;
using Pingboard.Application.Observability;
using Pingboard.Application.Probing;

namespace Pingboard.Application;

/// <summary>
///     Методы расширения для регистрации сценариев и сервисов слоя Application в DI-контейнере.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(PrometheusMetricsExporter.Default);
        services.TryAddSingleton<IPingboardMetrics, PingboardMetrics>();

        services.AddScoped<CreateMonitor>();
        services.AddScoped<ListMonitors>();
        services.AddScoped<GetMonitor>();
        services.AddScoped<UpdateMonitor>();
        services.AddScoped<DeleteMonitor>();
        services.AddScoped<GetMonitorChecks>();

        services.AddScoped<RegisterUser>();
        services.AddScoped<LoginUser>();

        services.AddScoped<RunDueChecks>();

        services.TryAddTimeProvider();
        services.TryAddScoped<ICurrentUser, MissingCurrentUser>();

        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider))) services.AddSingleton(TimeProvider.System);
    }
}
