using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pingboard.Worker.Hosting.Logging;

namespace Pingboard.Application.Tests.Worker;

public sealed class JsonConsoleLoggerTests
{
    private static (ILogger Logger, StringWriter Output) CreateLogger(string category = "TestCategory")
    {
        var output = new StringWriter();
        var provider = new JsonConsoleLoggerProvider(output);
        return (provider.CreateLogger(category), output);
    }

    [Fact]
    public void Log_StructuredParameters_IncludedInState()
    {
        var (logger, output) = CreateLogger();

        logger.LogInformation("Monitors checked: {Checked}, failed: {Failed}", 5, 1);

        var line = output.ToString().Trim();
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        Assert.Equal("Information", root.GetProperty("LogLevel").GetString());
        Assert.Equal("TestCategory", root.GetProperty("Category").GetString());
        Assert.Equal("Monitors checked: 5, failed: 1", root.GetProperty("Message").GetString());

        Assert.True(root.TryGetProperty("State", out var state));
        Assert.Equal(5, state.GetProperty("Checked").GetInt32());
        Assert.Equal(1, state.GetProperty("Failed").GetInt32());
        Assert.False(root.TryGetProperty("Event", out _));
    }

    [Fact]
    public void Log_WithEventName_IncludesEventProperty()
    {
        var (logger, output) = CreateLogger();

        logger.Log(LogLevel.Warning, new EventId(123, "StartupCheck"), "Something occurred");

        var line = output.ToString().Trim();
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        Assert.Equal("Warning", root.GetProperty("LogLevel").GetString());
        Assert.Equal(123, root.GetProperty("EventId").GetInt32());
        Assert.Equal("StartupCheck", root.GetProperty("Event").GetString());
        Assert.Equal("Something occurred", root.GetProperty("Message").GetString());
    }

    [Fact]
    public void Log_WithException_IncludesExceptionProperty()
    {
        var (logger, output) = CreateLogger();
        var ex = new InvalidOperationException("DB failed");

        logger.LogError(ex, "Failed query");

        var line = output.ToString().Trim();
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        Assert.Equal("Error", root.GetProperty("LogLevel").GetString());
        Assert.True(root.TryGetProperty("Exception", out var exElem));
        Assert.Contains("DB failed", exElem.GetString());
    }

    [Fact]
    public void Log_WithScope_IncludesScopesProperty()
    {
        var (logger, output) = CreateLogger();

        using (logger.BeginScope("BatchId: {BatchId}", 42))
        {
            logger.LogInformation("Processing batch");
        }

        var line = output.ToString().Trim();
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("Scopes", out var scopes));
        Assert.Equal(JsonValueKind.Array, scopes.ValueKind);
        Assert.Equal(1, scopes.GetArrayLength());
        var scopeItem = scopes[0];
        Assert.Equal(42, scopeItem.GetProperty("BatchId").GetInt32());
    }
}
