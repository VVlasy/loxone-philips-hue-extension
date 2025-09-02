using Serilog.Core;
using Serilog.Events;
using LoxoneHueBridge.Web.Services;
using Microsoft.AspNetCore.SignalR;

namespace LoxoneHueBridge.Web.Infrastructure;

public class LogCollectorSink : ILogEventSink
{
    private readonly ILogCollectorService _logCollector;
    private readonly IHubContext<LoggingHub>? _hubContext;

    public LogCollectorSink(ILogCollectorService logCollector, IHubContext<LoggingHub>? hubContext = null)
    {
        _logCollector = logCollector;
        _hubContext = hubContext;
    }

    public void Emit(LogEvent logEvent)
    {
        try
        {
            var logEntry = new LogEntry
            {
                Timestamp = logEvent.Timestamp.DateTime,
                Level = logEvent.Level.ToString(),
                Category = GetCategoryFromProperties(logEvent),
                Message = logEvent.RenderMessage(),
                Exception = logEvent.Exception?.ToString(),
                Properties = ExtractProperties(logEvent)
            };

            // Add to collector
            _logCollector.AddLogEntry(logEntry);

            // Send to SignalR hub for real-time updates - use "Logs" group to match the frontend
            _hubContext?.Clients.Group("Logs").SendAsync("LogEvent", logEntry);
        }
        catch
        {
            // Ignore errors in logging sink to prevent infinite loops
        }
    }

    private static string GetCategoryFromProperties(LogEvent logEvent)
    {
        if (logEvent.Properties.TryGetValue("SourceContext", out var sourceContext))
        {
            return sourceContext.ToString().Trim('"');
        }
        return "Application";
    }

    private static Dictionary<string, object> ExtractProperties(LogEvent logEvent)
    {
        var properties = new Dictionary<string, object>();
        
        foreach (var property in logEvent.Properties)
        {
            if (property.Key != "SourceContext")
            {
                properties[property.Key] = property.Value.ToString().Trim('"');
            }
        }

        return properties;
    }
}

public static class LogCollectorSinkExtensions
{
    public static Serilog.LoggerConfiguration LogCollectorSink(
        this Serilog.Configuration.LoggerSinkConfiguration sinkConfiguration,
        ILogCollectorService logCollector,
        IHubContext<LoggingHub>? hubContext = null)
    {
        return sinkConfiguration.Sink(new LogCollectorSink(logCollector, hubContext));
    }
}
