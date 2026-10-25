using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pingboard.Application.Abstractions;
using Pingboard.Application.Observability;
using Pingboard.Worker.Options;

namespace Pingboard.Worker.Hosting;

/// <summary>
///     Легковесный HTTP-сервер экспортера метрик Prometheus для фонового воркера (по умолчанию порт 9100, маршрут /metrics).
///     Реализован поверх <see cref="TcpListener" />, что не требует прав администратора в Windows и root в контейнерах Linux.
/// </summary>
public sealed class WorkerMetricsServer(
    PrometheusMetricsExporter exporter,
    IPingboardMetrics metrics,
    IOptions<WorkerOptions> options,
    ILogger<WorkerMetricsServer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Гарантируем инициализацию инструментов бизнес-метрик на старте воркера.
        _ = metrics;

        var port = options.Value.MetricsPort;
        if (port <= 0)
            return;

        using var listener = new TcpListener(IPAddress.Any, port);

        try
        {
            listener.Start();
            logger.LogInformation("Worker Prometheus metrics server listening on port {Port} (/metrics)", port);
        }
        catch (SocketException ex)
        {
            logger.LogWarning(ex, "Failed to bind worker metrics port {Port}; metrics endpoint is disabled", port);
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleClientAsync(client, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка сервера метрик.
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

                var requestLine = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(requestLine))
                    return;

                // Дочитываем заголовки HTTP-запроса до пустой строки.
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(ct)))
                {
                }

                var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var path = parts.Length >= 2 ? parts[1] : "/";

                if (path.StartsWith("/metrics", StringComparison.Ordinal))
                {
                    var payload = exporter.Scrape();
                    await WriteResponseAsync(stream, "200 OK", PrometheusMetricsExporter.ContentType, payload, ct);
                }
                else if (path.StartsWith("/health", StringComparison.Ordinal))
                {
                    await WriteResponseAsync(stream, "200 OK", "application/json; charset=utf-8", "{\"status\":\"ok\"}", ct);
                }
                else
                {
                    await WriteResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "Not Found\n", ct);
                }
            }
            catch (Exception)
            {
                // Разрыв соединения клиентом при скрейпинге не влияет на работу воркера.
            }
        }
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        string status,
        string contentType,
        string body,
        CancellationToken ct)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header = string.Create(
            CultureInfo.InvariantCulture,
            $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n");
        var headerBytes = Encoding.ASCII.GetBytes(header);

        await stream.WriteAsync(headerBytes, ct);
        await stream.WriteAsync(bodyBytes, ct);
        await stream.FlushAsync(ct);
    }
}
