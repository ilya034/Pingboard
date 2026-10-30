using System.Runtime.InteropServices;
using System.Text.Json;
using Pingboard.Prober;

var options = ProberOptions.FromEnvironment();
var exporter = new ProberMetricsExporter();
var metricsServer = new ProberMetricsServer(exporter, options.MetricsPort);
var runner = new SyntheticProberRunner(options, exporter);

using var cts = new CancellationTokenSource();

using var sigterm = PosixSignalRegistration.Create(
    PosixSignal.SIGTERM,
    ctx =>
    {
        ctx.Cancel = true;
        ProberLog.Info("Received SIGTERM: stopping synthetic prober");
        cts.Cancel();
    });

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    ProberLog.Info("Received Ctrl+C: stopping synthetic prober");
    cts.Cancel();
};

await Task.WhenAll(
    metricsServer.RunAsync(cts.Token),
    runner.RunAsync(cts.Token));

internal static class ProberLog
{
    public static void Info(string message) => Write("Information", message);

    public static void Warn(string message) => Write("Warning", message);

    private static void Write(string level, string message)
    {
        var entry = JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            level,
            category = "Pingboard.Prober",
            message
        });
        Console.WriteLine(entry);
    }
}
