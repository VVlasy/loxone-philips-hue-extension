using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using LoxoneHueBridge.Core.Configuration;
using LoxoneHueBridge.Core.Services;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace LoxoneHueBridge.Web.Pages
{
    public class SettingsModel : PageModel
    {
        private readonly IOptionsSnapshot<LoxoneHueBridgeConfig> _config;
        private readonly IConfiguration _configuration;
        private readonly IConfigurationUpdateService _configUpdateService;
        private readonly ILogger<SettingsModel> _logger;

        public SettingsModel(
            IOptionsSnapshot<LoxoneHueBridgeConfig> config, 
            IConfiguration configuration, 
            IConfigurationUpdateService configUpdateService,
            ILogger<SettingsModel> logger)
        {
            _config = config;
            _configuration = configuration;
            _configUpdateService = configUpdateService;
            _logger = logger;
        }

        public LoxoneHueBridgeConfig Settings { get; set; } = new();
        public SystemInformation SystemInfo { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }

        public void OnGet()
        {
            Settings = _config.Value;
            LoadSystemInformation();
        }

        public async Task<IActionResult> OnPostUpdateCanAsync(string canInterface, int canBitrate, bool mockMode)
        {
            try
            {
                // Update configuration
                _configuration[$"LoxoneHueBridge:CanInterface"] = canInterface;
                _configuration[$"LoxoneHueBridge:CanBitrate"] = canBitrate.ToString();
                _configuration[$"LoxoneHueBridge:MockMode"] = mockMode.ToString();

                // TODO: Restart CAN service with new settings
                SuccessMessage = "CAN settings updated successfully. Restart the application to apply changes.";
                
                _logger.LogInformation("CAN settings updated: Interface={Interface}, Bitrate={Bitrate}, MockMode={MockMode}", 
                    canInterface, canBitrate, mockMode);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error updating CAN settings: {ex.Message}";
                _logger.LogError(ex, "Error updating CAN settings");
            }

            Settings = _config.Value;
            LoadSystemInformation();
            return Page();
        }

        public async Task<IActionResult> OnPostUpdateHueAsync(string applicationName, string deviceName)
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(applicationName) || string.IsNullOrWhiteSpace(deviceName))
                {
                    ErrorMessage = "Application name and device name are required.";
                    Settings = _config.Value;
                    LoadSystemInformation();
                    return Page();
                }

                // Update only the application identity settings
                // Note: Bridge IP and pairing are managed through the pairing page
                _configuration[$"LoxoneHueBridge:HueBridge:ApplicationName"] = applicationName;
                _configuration[$"LoxoneHueBridge:HueBridge:DeviceName"] = deviceName;

                SuccessMessage = "Application identity settings updated successfully.";
                
                _logger.LogInformation("Hue identity settings updated: AppName={AppName}, DeviceName={DeviceName}", 
                    applicationName, deviceName);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error updating Hue settings: {ex.Message}";
                _logger.LogError(ex, "Error updating Hue identity settings");
            }

            Settings = _config.Value;
            LoadSystemInformation();
            return Page();
        }

        public async Task<IActionResult> OnPostUpdateLoggingAsync(string logLevel, int logRetentionDays, bool enableFileLogging)
        {
            try
            {
                // Update configuration
                _configuration[$"Serilog:MinimumLevel:Default"] = logLevel;
                _configuration[$"LoxoneHueBridge:LogRetentionDays"] = logRetentionDays.ToString();
                _configuration[$"LoxoneHueBridge:EnableFileLogging"] = enableFileLogging.ToString();

                SuccessMessage = "Logging settings updated successfully. Some changes may require application restart.";
                
                _logger.LogInformation("Logging settings updated: Level={LogLevel}, Retention={Retention}, FileLogging={FileLogging}", 
                    logLevel, logRetentionDays, enableFileLogging);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error updating logging settings: {ex.Message}";
                _logger.LogError(ex, "Error updating logging settings");
            }

            Settings = _config.Value;
            LoadSystemInformation();
            return Page();
        }

        private void LoadSystemInformation()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version?.ToString() ?? "Unknown";
                var dotnetVersion = Environment.Version.ToString();
                var platform = RuntimeInformation.OSDescription;
                var uptime = DateTime.Now - Process.GetCurrentProcess().StartTime;

                SystemInfo = new SystemInformation
                {
                    Version = version,
                    DotNetVersion = dotnetVersion,
                    Platform = platform,
                    Uptime = $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m",
                    CanStatus = _config.Value.MockMode ? "Mock Mode" : "Unknown",
                    HueStatus = !string.IsNullOrEmpty(_config.Value.HueBridge.AppKey) ? "Paired" : "Not Paired",
                    ActiveMappings = 0, // TODO: Get from mapping service
                    MemoryUsage = GC.GetTotalMemory(false) / 1024 / 1024 // MB
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading system information");
                SystemInfo = new SystemInformation { Version = "Error loading info" };
            }
        }
    }

    public class SystemInformation
    {
        public string Version { get; set; } = "";
        public string DotNetVersion { get; set; } = "";
        public string Platform { get; set; } = "";
        public string Uptime { get; set; } = "";
        public string CanStatus { get; set; } = "";
        public string HueStatus { get; set; } = "";
        public int ActiveMappings { get; set; }
        public long MemoryUsage { get; set; }
    }
}
