using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace LoxoneHueBridge.Web.Services;

public interface ILogCollectorService
{
    void AddLogEntry(LogEntry logEntry);
    List<LogEntry> GetRecentLogs(int maxCount = 100);
    List<LogEntry> GetLogsByLevel(LogLevel minLevel, int maxCount = 100);
    List<LogEntry> GetLogsByCategory(string category, int maxCount = 100);
    void ClearLogs();
    LogStatistics GetStatistics();
}

public class LogCollectorService : ILogCollectorService
{
    private readonly ConcurrentQueue<LogEntry> _logs = new();
    private readonly object _lock = new();
    private int _canFrameCount = 0;
    private int _hueCommandCount = 0;
    private int _errorCount = 0;
    private int _totalCount = 0;
    
    private const int MaxLogEntries = 1000;

    public void AddLogEntry(LogEntry logEntry)
    {
        lock (_lock)
        {
            _logs.Enqueue(logEntry);
            _totalCount++;

            // Update statistics
            if (logEntry.Category?.Contains("CAN", StringComparison.OrdinalIgnoreCase) == true ||
                logEntry.Message?.Contains("CAN frame", StringComparison.OrdinalIgnoreCase) == true)
            {
                _canFrameCount++;
            }

            if (logEntry.Category?.Contains("Hue", StringComparison.OrdinalIgnoreCase) == true ||
                logEntry.Message?.Contains("Hue", StringComparison.OrdinalIgnoreCase) == true)
            {
                _hueCommandCount++;
            }

            if (logEntry.Level == "Error" || logEntry.Level == "Critical")
            {
                _errorCount++;
            }

            // Remove old entries if we exceed the maximum
            while (_logs.Count > MaxLogEntries)
            {
                _logs.TryDequeue(out _);
            }
        }
    }

    public List<LogEntry> GetRecentLogs(int maxCount = 100)
    {
        var allLogs = _logs.ToArray();
        return allLogs
            .OrderByDescending(l => l.Timestamp)
            .Take(maxCount)
            .OrderBy(l => l.Timestamp)
            .ToList();
    }

    public List<LogEntry> GetLogsByLevel(LogLevel minLevel, int maxCount = 100)
    {
        var allLogs = _logs.ToArray();
        return allLogs
            .Where(l => GetLogLevel(l.Level) >= minLevel)
            .OrderByDescending(l => l.Timestamp)
            .Take(maxCount)
            .OrderBy(l => l.Timestamp)
            .ToList();
    }

    public List<LogEntry> GetLogsByCategory(string category, int maxCount = 100)
    {
        var allLogs = _logs.ToArray();
        return allLogs
            .Where(l => l.Category?.Contains(category, StringComparison.OrdinalIgnoreCase) == true)
            .OrderByDescending(l => l.Timestamp)
            .Take(maxCount)
            .OrderBy(l => l.Timestamp)
            .ToList();
    }

    public void ClearLogs()
    {
        lock (_lock)
        {
            _logs.Clear();
            _canFrameCount = 0;
            _hueCommandCount = 0;
            _errorCount = 0;
            _totalCount = 0;
        }
    }

    public LogStatistics GetStatistics()
    {
        lock (_lock)
        {
            var allLogs = _logs.ToArray();
            
            var logsByLevel = allLogs
                .GroupBy(log => log.Level)
                .ToDictionary(g => g.Key, g => g.Count());

            var logsByCategory = allLogs
                .GroupBy(log => log.Category)
                .ToDictionary(g => g.Key, g => g.Count());

            return new LogStatistics
            {
                CanFrameCount = _canFrameCount,
                HueCommandCount = _hueCommandCount,
                ErrorCount = _errorCount,
                TotalCount = _totalCount,
                LogsByLevel = logsByLevel,
                LogsByCategory = logsByCategory,
                OldestLogTime = allLogs.MinBy(l => l.Timestamp)?.Timestamp,
                NewestLogTime = allLogs.MaxBy(l => l.Timestamp)?.Timestamp
            };
        }
    }

    private static LogLevel GetLogLevel(string levelName)
    {
        return levelName switch
        {
            "Critical" => LogLevel.Critical,
            "Error" => LogLevel.Error,
            "Warning" => LogLevel.Warning,
            "Information" => LogLevel.Information,
            "Debug" => LogLevel.Debug,
            "Trace" => LogLevel.Trace,
            _ => LogLevel.None
        };
    }
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Exception { get; set; }
    public Dictionary<string, object>? Properties { get; set; }
}

public class LogStatistics
{
    public int CanFrameCount { get; set; }
    public int HueCommandCount { get; set; }
    public int ErrorCount { get; set; }
    public int TotalCount { get; set; }
    public Dictionary<string, int> LogsByLevel { get; set; } = new();
    public Dictionary<string, int> LogsByCategory { get; set; } = new();
    public DateTime? OldestLogTime { get; set; }
    public DateTime? NewestLogTime { get; set; }
}
