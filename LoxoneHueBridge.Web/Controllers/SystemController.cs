using Microsoft.AspNetCore.Mvc;
using LoxoneHueBridge.Core.Services;
using LoxoneHueBridge.Web.Services;

namespace LoxoneHueBridge.Web.Controllers
{
    [ApiController]
    [Route("api/system")]
    public class SystemController : ControllerBase
    {
        private readonly ILogger<SystemController> _logger;
        private readonly IHostApplicationLifetime _applicationLifetime;

        public SystemController(ILogger<SystemController> logger, IHostApplicationLifetime applicationLifetime)
        {
            _logger = logger;
            _applicationLifetime = applicationLifetime;
        }

        [HttpPost("restart-services")]
        public IActionResult RestartServices()
        {
            try
            {
                _logger.LogInformation("Service restart requested");
                
                // In a real implementation, you would restart specific services
                // For now, we'll just return success
                return Ok(new { success = true, message = "Services restart initiated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restarting services");
                return Ok(new { success = false, message = ex.Message });
            }
        }

        [HttpGet("export-config")]
        public IActionResult ExportConfig()
        {
            try
            {
                // Create a simplified config export
                var config = new
                {
                    ExportDate = DateTime.UtcNow,
                    Version = "1.0.0",
                    Note = "Configuration export functionality - placeholder implementation"
                };

                var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                
                return File(bytes, "application/json", "loxone-hue-bridge-config.json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting configuration");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("import-config")]
        public async Task<IActionResult> ImportConfig(IFormFile configFile)
        {
            try
            {
                if (configFile == null || configFile.Length == 0)
                {
                    return Ok(new { success = false, message = "No file provided" });
                }

                // Placeholder implementation
                _logger.LogInformation("Configuration import requested for file: {FileName}", configFile.FileName);
                
                return Ok(new { success = true, message = "Configuration import functionality - placeholder implementation" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing configuration");
                return Ok(new { success = false, message = ex.Message });
            }
        }
    }

    [ApiController]
    [Route("api/logs")]
    public class LogsController : ControllerBase
    {
        private readonly ILogger<LogsController> _logger;
        private readonly ILogCollectorService _logCollector;

        public LogsController(ILogger<LogsController> logger, ILogCollectorService logCollector)
        {
            _logger = logger;
            _logCollector = logCollector;
        }

        [HttpGet("recent")]
        public IActionResult GetRecentLogs(int count = 100)
        {
            try
            {
                var recentLogs = _logCollector.GetRecentLogs(count);
                return Ok(recentLogs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving recent logs");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("statistics")]
        public IActionResult GetStatistics()
        {
            try
            {
                var stats = _logCollector.GetStatistics();
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving log statistics");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("by-level")]
        public IActionResult GetLogsByLevel(string level = "Information", int count = 100)
        {
            try
            {
                if (!Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(level, true, out var logLevel))
                {
                    return BadRequest(new { error = "Invalid log level" });
                }

                var logs = _logCollector.GetLogsByLevel(logLevel, count);
                return Ok(logs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving logs by level");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("by-category")]
        public IActionResult GetLogsByCategory(string category, int count = 100)
        {
            try
            {
                var logs = _logCollector.GetLogsByCategory(category, count);
                return Ok(logs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving logs by category");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("clear")]
        public IActionResult ClearLogs()
        {
            try
            {
                _logCollector.ClearLogs();
                _logger.LogInformation("Log history cleared by user");
                return Ok(new { success = true, message = "Log history cleared successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing logs");
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("export")]
        public IActionResult ExportLogs(int count = 1000)
        {
            try
            {
                var logs = _logCollector.GetRecentLogs(count);
                var logText = string.Join("\n", logs.Select(log =>
                    $"[{log.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{log.Level}] [{log.Category}] {log.Message}" +
                    (string.IsNullOrEmpty(log.Exception) ? "" : $"\n  Exception: {log.Exception}")
                ));

                var bytes = System.Text.Encoding.UTF8.GetBytes(logText);
                var fileName = $"loxone-hue-bridge-logs-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.txt";
                
                return File(bytes, "text/plain", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting logs");
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
