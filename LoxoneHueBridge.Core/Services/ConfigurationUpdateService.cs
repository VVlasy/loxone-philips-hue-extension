using LoxoneHueBridge.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace LoxoneHueBridge.Core.Services;

public class ConfigurationUpdateService : IConfigurationUpdateService
{
    private readonly ILogger<ConfigurationUpdateService> _logger;
    private readonly IOptionsMonitor<LoxoneHueBridgeConfig> _config;
    private readonly IConfiguration _configuration;
    private readonly string _configFilePath;

    public ConfigurationUpdateService(
        ILogger<ConfigurationUpdateService> logger,
        IOptionsMonitor<LoxoneHueBridgeConfig> config,
        IConfiguration configuration)
    {
        _logger = logger;
        _config = config;
        _configuration = configuration;
        
        // Try to find the appsettings.json path
        _configFilePath = FindConfigFilePath();
    }

    public async Task UpdateHueBridgeConfigAsync(string? ipAddress = null, string? appKey = null, bool? autoDiscover = null)
    {
        try
        {
            var configData = await LoadConfigFileAsync();
            
            // Update the LoxoneHueBridge.HueBridge section (correct path)
            if (!configData.ContainsKey("LoxoneHueBridge"))
            {
                configData["LoxoneHueBridge"] = new Dictionary<string, object>();
            }
            
            var loxoneSection = (Dictionary<string, object>)configData["LoxoneHueBridge"];
            
            if (!loxoneSection.ContainsKey("HueBridge"))
            {
                loxoneSection["HueBridge"] = new Dictionary<string, object>();
            }
            
            var hueBridgeSection = (Dictionary<string, object>)loxoneSection["HueBridge"];
            
            if (ipAddress != null)
            {
                hueBridgeSection["ManualIpAddress"] = ipAddress;
                _logger.LogInformation("Updated HueBridge IP address to: {IpAddress}", ipAddress);
            }
            
            if (appKey != null)
            {
                hueBridgeSection["AppKey"] = appKey;
                _logger.LogInformation("Updated HueBridge AppKey");
            }
            
            if (autoDiscover.HasValue)
            {
                hueBridgeSection["AutoDiscover"] = autoDiscover.Value;
                _logger.LogInformation("Updated HueBridge AutoDiscover to: {AutoDiscover}", autoDiscover.Value);
            }
            
            await SaveConfigFileAsync(configData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update HueBridge configuration");
            throw;
        }
    }

    public async Task SaveConfigurationAsync()
    {
        // This method can be used for future extensions
        await Task.CompletedTask;
    }

    private string FindConfigFilePath()
    {
        // Common configuration file locations
        var possiblePaths = new[]
        {
            "appsettings.json",
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                _logger.LogDebug("Found configuration file at: {Path}", path);
                return path;
            }
        }

        // Default to current directory
        var defaultPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        _logger.LogWarning("Configuration file not found, using default path: {Path}", defaultPath);
        return defaultPath;
    }

    private async Task<Dictionary<string, object>> LoadConfigFileAsync()
    {
        if (!File.Exists(_configFilePath))
        {
            _logger.LogInformation("Configuration file not found, creating new one: {Path}", _configFilePath);
            return new Dictionary<string, object>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_configFilePath);
            var config = JsonSerializer.Deserialize<Dictionary<string, object>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            return config ?? new Dictionary<string, object>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load configuration file: {Path}", _configFilePath);
            return new Dictionary<string, object>();
        }
    }

    private async Task SaveConfigFileAsync(Dictionary<string, object> configData)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
            
            var json = JsonSerializer.Serialize(configData, options);
            await File.WriteAllTextAsync(_configFilePath, json);
            
            _logger.LogInformation("Configuration saved to: {Path}", _configFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration file: {Path}", _configFilePath);
            throw;
        }
    }
}
