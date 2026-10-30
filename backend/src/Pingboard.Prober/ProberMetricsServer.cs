using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Pingboard.Prober;

/// <summary>
///     Встроенный HTTP-сервер пробера для отдачи метрик Prometheus (<c>/metrics</c>) и liveness-проверки (<c>/healthz</c>).
/// </summary>
public sealed class ProberMetricsServer(ProberMetricsExporter exporter, int port)
{
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        if (port <= 0)
            return;

        using var listener = new TcpListener(IPAddress.Any, port);

        try
        {
            listener.Start();
            ProberLog.Info($"Prober metrics server listening on port {port} (/metrics, /healthz)");
        }
        catch (SocketException ex)
        {
            ProberLog.Warn($"Failed to bind prober metrics port {port}: {ex.Message}");
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
            // Штатная остановка по сигналу завершения.
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
                using var reader = new StreamReader(
                    stream,
                    Encoding.ASCII,
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 1024,
                    leaveOpen: true);

                var requestLine = await reader.ReadLineAsync(ct);
                if (string.IsNullOrEmpty(requestLine))
                    return;

                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(ct)))
                {
                }

                var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var path = parts.Length >= 2 ? parts[1] : "/";

                if (path.StartsWith("/metrics", StringComparison.Ordinal))
                {
                    var payload = exporter.Scrape();
                    await WriteResponseAsync(stream, "200 OK", ProberMetricsExporter.ContentType, payload, ct);
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
                // Разрыв соединения клиентом не влияет на работу пробера.
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
