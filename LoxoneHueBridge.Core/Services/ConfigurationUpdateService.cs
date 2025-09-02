using LoxoneHueBridge.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LoxoneHueBridge.Core.Services;

public class ConfigurationUpdateService : IConfigurationUpdateService
{
    private readonly ILogger<ConfigurationUpdateService> _logger;
    private readonly IOptionsMonitor<LoxoneHueBridgeConfig> _config;
    private readonly ConfigurationWriter _configWriter;

    public ConfigurationUpdateService(
        ILogger<ConfigurationUpdateService> logger,
        IOptionsMonitor<LoxoneHueBridgeConfig> config,
        ConfigurationWriter configWriter)
    {
        _logger = logger;
        _config = config;
        _configWriter = configWriter;
    }

    public async Task UpdateHueBridgeConfigAsync(string? ipAddress = null, string? appKey = null, bool? autoDiscover = null)
    {
        try
        {
            var updates = new Dictionary<string, object?>();

            // For unpairing operation, we need to handle null values explicitly
            // Check the stack trace to see if this is being called from UnpairFromBridgeAsync
            var stackTrace = Environment.StackTrace;
            var isUnpairingOperation = stackTrace.Contains("UnpairFromBridgeAsync");

            // Always update if explicitly called from unpairing (even with null values)
            // Otherwise, only update if value is not null
            if (isUnpairingOperation || ipAddress != null)
            {
                updates["LoxoneHueBridge:HueBridge:ManualIpAddress"] = ipAddress;
                _logger.LogInformation("Updated HueBridge IP address to: {IpAddress}", ipAddress ?? "(cleared)");
            }

            if (isUnpairingOperation || appKey != null)
            {
                updates["LoxoneHueBridge:HueBridge:AppKey"] = appKey;
                _logger.LogInformation("Updated HueBridge AppKey: {Status}", appKey != null ? "Set" : "Cleared");
            }

            if (autoDiscover.HasValue)
            {
                updates["LoxoneHueBridge:HueBridge:AutoDiscover"] = autoDiscover.Value;
                _logger.LogInformation("Updated HueBridge AutoDiscover to: {AutoDiscover}", autoDiscover.Value);
            }

            if (updates.Count > 0)
            {
                await _configWriter.UpdateConfigurationAsync(updates);
                _logger.LogInformation("Configuration changes saved successfully");
            }
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
}
