using HueApi;
using HueApi.BridgeLocator;
using HueApi.Models;
using HueApi.Models.Requests;
using HueApi.ColorConverters;
using LoxoneHueBridge.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HueApi.ColorConverters.Original.Extensions;

namespace LoxoneHueBridge.Core.Services;

public interface IHueService
{
    Task<bool> DiscoverBridgeAsync(CancellationToken cancellationToken = default);
    Task<List<DiscoveredBridge>> DiscoverBridgesAsync(CancellationToken cancellationToken = default);
    Task<string?> PairWithBridgeAsync(CancellationToken cancellationToken = default);
    Task<bool> PairWithBridgeAsync(string bridgeIp, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Light>> GetLightsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<GroupedLight>> GetGroupsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<HueApi.Models.Scene>> GetScenesAsync(CancellationToken cancellationToken = default);
    Task<bool> SetLightStateAsync(Guid lightId, bool on, byte? brightness = null, CancellationToken cancellationToken = default);
    Task<bool> SetLightStateAsync(string lightId, bool on, byte? brightness = null, CancellationToken cancellationToken = default);
    Task<bool> SetLightColorAsync(Guid lightId, byte red, byte green, byte blue, byte? brightness = null, CancellationToken cancellationToken = default);
    Task<bool> SetLightColorAsync(string lightId, byte red, byte green, byte blue, byte? brightness = null, CancellationToken cancellationToken = default);
    Task<bool> SetGroupStateAsync(Guid groupId, bool on, byte? brightness = null, CancellationToken cancellationToken = default);
    Task<bool> SetGroupStateAsync(string groupId, bool on, byte? brightness = null, CancellationToken cancellationToken = default);
    Task<bool> ActivateSceneAsync(Guid sceneId, CancellationToken cancellationToken = default);
    Task<bool> ActivateSceneAsync(string sceneId, CancellationToken cancellationToken = default);
    Task<BridgeStatus?> GetBridgeStatusAsync();
    Task UnpairFromBridgeAsync();
    BridgeStatus? BridgeStatus { get; }
}

public class HueService : IHueService
{
    private readonly ILogger<HueService> _logger;
    private readonly IOptionsMonitor<LoxoneHueBridgeConfig> _config;
    private readonly IConfigurationUpdateService _configUpdateService;
    private LocalHueApi? _client;
    private BridgeStatus? _bridgeStatus = null;

    public BridgeStatus? BridgeStatus => _bridgeStatus;

    public HueService(ILogger<HueService> logger, IOptionsMonitor<LoxoneHueBridgeConfig> config, IConfigurationUpdateService configUpdateService)
    {
        _logger = logger;
        _config = config;
        _configUpdateService = configUpdateService;
        
        // Initialize bridge status with current configuration
        UpdateBridgeStatusFromConfig();
    }

    private void UpdateBridgeStatusFromConfig()
    {
        var config = _config.CurrentValue.HueBridge;
        
        // If we have a manual IP address or auto-discover is disabled, use the configured IP
        if (!string.IsNullOrEmpty(config.ManualIpAddress))
        {
            _bridgeStatus = new BridgeStatus
            {
                IpAddress = config.ManualIpAddress,
                IsPaired = !string.IsNullOrEmpty(config.AppKey),
                AppKey = config.AppKey,
                IsConnected = false
            };
            _logger.LogInformation("Loaded bridge configuration: IP={IpAddress}, Paired={IsPaired}", 
                _bridgeStatus.IpAddress, _bridgeStatus.IsPaired);
        }
        else if (!string.IsNullOrEmpty(config.AppKey))
        {
            // We have credentials but no IP - this means we were paired before but IP was lost
            _bridgeStatus = new BridgeStatus
            {
                IsPaired = true,
                AppKey = config.AppKey,
                IsConnected = false
            };
            _logger.LogWarning("Found AppKey but no bridge IP address - bridge will need to be rediscovered");
        }
        // If no manual IP and no app key, leave _bridgeStatus as null (no bridge discovered)
    }

    public async Task<bool> DiscoverBridgeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var config = _config.CurrentValue.HueBridge;
            
            if (!config.AutoDiscover && !string.IsNullOrEmpty(config.ManualIpAddress))
            {
                _bridgeStatus = new BridgeStatus
                {
                    IpAddress = config.ManualIpAddress,
                    IsPaired = !string.IsNullOrEmpty(config.AppKey),
                    AppKey = config.AppKey,
                    IsConnected = false
                };
                _logger.LogInformation("Using manual Hue Bridge IP: {BridgeIp}", _bridgeStatus.IpAddress);
                return true;
            }

            _logger.LogInformation("Discovering Hue Bridge on network...");
            
            var bridges = await HueBridgeDiscovery.FastDiscoveryWithNetworkScanFallbackAsync(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
            
            var bridge = bridges.FirstOrDefault();
            if (bridge == null)
            {
                _logger.LogWarning("No Hue Bridge found on network");
                return false;
            }

            _bridgeStatus = new BridgeStatus
            {
                IpAddress = bridge.IpAddress,
                BridgeId = bridge.BridgeId,
                IsPaired = !string.IsNullOrEmpty(_config.CurrentValue.HueBridge.AppKey),
                AppKey = _config.CurrentValue.HueBridge.AppKey,
                IsConnected = false
            };
            _logger.LogInformation("Discovered Hue Bridge at IP: {BridgeIp}", _bridgeStatus.IpAddress);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover Hue Bridge");
            return false;
        }
    }

    public async Task<string?> PairWithBridgeAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_bridgeStatus?.IpAddress))
        {
            _logger.LogWarning("Cannot pair: Bridge IP not set. Run discovery first.");
            return null;
        }

        try
        {
            var config = _config.CurrentValue.HueBridge;
            _logger.LogInformation("Attempting to pair with Hue Bridge at {BridgeIp}...", _bridgeStatus.IpAddress);
            
            var result = await LocalHueApi.RegisterAsync(_bridgeStatus.IpAddress, config.ApplicationName, config.DeviceName);
            
            if (result?.Username != null)
            {
                var appKey = result.Username;
                _logger.LogInformation("Successfully paired with Hue Bridge. App Key: {AppKey}", appKey);
                
                // Persist the bridge configuration
                await _configUpdateService.UpdateHueBridgeConfigAsync(
                    ipAddress: _bridgeStatus.IpAddress,
                    appKey: appKey,
                    autoDiscover: false // Set to false since we now have a known bridge
                );
                
                // Initialize client with new app key
                _client = new LocalHueApi(_bridgeStatus.IpAddress, appKey);
                
                // Update bridge status with pairing info
                _bridgeStatus.IsPaired = true;
                _bridgeStatus.AppKey = appKey;
                _bridgeStatus.IsConnected = true;
                
                return appKey;
            }
            else
            {
                _logger.LogWarning("Pairing failed. Make sure to press the button on the Hue Bridge and try again.");
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pair with Hue Bridge");
            return null;
        }
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_client == null)
            {
                var appKey = _config.CurrentValue.HueBridge.AppKey;
                if (string.IsNullOrEmpty(_bridgeStatus?.IpAddress))
                {
                    if (_bridgeStatus != null)
                    {
                        _bridgeStatus.IsConnected = false;
                        _bridgeStatus.IsPaired = false;
                    }
                    return false;
                }
                
                if (string.IsNullOrEmpty(appKey))
                {
                    // Can reach bridge but not paired
                    _bridgeStatus.IsConnected = true;
                    _bridgeStatus.IsPaired = false;
                    return false;
                }
                
                _client = new LocalHueApi(_bridgeStatus.IpAddress, appKey);
            }

            var config = await _client.GetBridgeAsync();
            if (_bridgeStatus != null)
            {
                _bridgeStatus.IsConnected = true;
                _bridgeStatus.IsPaired = true;
                _bridgeStatus.ApiVersion = "";
            }
            _logger.LogDebug("Connection test successful. Bridge name: {BridgeName}", config?.Data?.FirstOrDefault()?.Metadata?.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection test failed");
            // Could be connection issue or authentication issue
            if (_bridgeStatus != null)
            {
                _bridgeStatus.IsConnected = false;
                // Keep paired status based on whether we have credentials
                _bridgeStatus.IsPaired = !string.IsNullOrEmpty(_config.CurrentValue.HueBridge.AppKey);
            }
            return false;
        }
    }

    public async Task<IEnumerable<Light>> GetLightsAsync(CancellationToken cancellationToken = default)
    {
        if (_client == null)
        {
            _logger.LogWarning("Cannot get lights: not connected to bridge");
            return Array.Empty<Light>();
        }

        try
        {
            var lights = await _client.GetLightsAsync();
            _logger.LogDebug("Retrieved {Count} lights from bridge", lights.Data.Count());
            return lights.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get lights from bridge");
            return Array.Empty<Light>();
        }
    }

    public async Task<IEnumerable<GroupedLight>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        if (_client == null)
        {
            _logger.LogWarning("Cannot get groups: not connected to bridge");
            return Array.Empty<GroupedLight>();
        }

        try
        {
            var groups = await _client.GetGroupedLightsAsync();
            _logger.LogDebug("Retrieved {Count} groups from bridge", groups.Data.Count());
            return groups.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get groups from bridge");
            return Array.Empty<GroupedLight>();
        }
    }

    public async Task<IEnumerable<HueApi.Models.Scene>> GetScenesAsync(CancellationToken cancellationToken = default)
    {
        if (_client == null)
        {
            _logger.LogWarning("Cannot get scenes: not connected to bridge");
            return Array.Empty<HueApi.Models.Scene>();
        }

        try
        {
            var scenes = await _client.GetScenesAsync();
            _logger.LogDebug("Retrieved {Count} scenes from bridge", scenes.Data.Count());
            return scenes.Data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get scenes from bridge");
            return Array.Empty<HueApi.Models.Scene>();
        }
    }

    public async Task<bool> SetLightStateAsync(Guid lightId, bool on, byte? brightness = null, CancellationToken cancellationToken = default)
    {
        if (_client == null)
        {
            _logger.LogWarning("Cannot control light: not connected to bridge");
            return false;
        }

        try
        {
            UpdateLight updateLight;
            
            if (brightness.HasValue && on)
            {
                updateLight = new UpdateLight().TurnOn().SetBrightness(brightness.Value / 254.0 * 100.0); // Convert from 0-254 to 0-100
            } 
            else if (on)
            {
                updateLight = new UpdateLight().TurnOn();
            }
            else
            {
                updateLight = new UpdateLight().TurnOff();
            }

            await _client.UpdateLightAsync(lightId, updateLight);
            _logger.LogDebug("Set light {LightId} state: On={On}, Brightness={Brightness}", lightId, on, brightness);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set light {LightId} state", lightId);
            return false;
        }
    }

    public async Task<bool> SetLightColorAsync(Guid lightId, byte red, byte green, byte blue, byte? brightness = null, CancellationToken cancellationToken = default)
    {
        if (_client == null)
        {
            _logger.LogWarning("Cannot control light: not connected to bridge");
            return false;
        }

        try
        {
            var updateLight = new UpdateLight()
                .TurnOn()
                .SetColor(new RGBColor(red, green, blue));
            
            if (brightness.HasValue)
            {
                updateLight.SetBrightness(brightness.Value / 254.0 * 100.0); // Convert from 0-254 to 0-100
            }

            await _client.UpdateLightAsync(lightId, updateLight);
            _logger.LogDebug("Set light {LightId} color: R={Red}, G={Green}, B={Blue}, Brightness={Brightness}", 
                lightId, red, green, blue, brightness);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set light {LightId} color", lightId);
            return false;
        }
    }

    public async Task<bool> SetGroupStateAsync(Guid groupId, bool on, byte? brightness = null, CancellationToken cancellationToken = default)
    {
        if (_client == null)
        {
            _logger.LogWarning("Cannot control group: not connected to bridge");
            return false;
        }

        try
        {
            UpdateGroupedLight updateGroupedLight;
            
            if (brightness.HasValue && on)
            {
                updateGroupedLight = new UpdateGroupedLight().TurnOn().SetBrightness(brightness.Value / 254.0 * 100.0); // Convert from 0-254 to 0-100
            } 
            else if (on)
            {
                updateGroupedLight = new UpdateGroupedLight().TurnOn();
            }
            else
            {
                updateGroupedLight = new UpdateGroupedLight().TurnOff();
            }

            await _client.UpdateGroupedLightAsync(groupId, updateGroupedLight);
            _logger.LogDebug("Set group {GroupId} state: On={On}, Brightness={Brightness}", groupId, on, brightness);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set group {GroupId} state", groupId);
            return false;
        }
    }

    public async Task<bool> ActivateSceneAsync(Guid sceneId, CancellationToken cancellationToken = default)
    {
        if (_client == null)
        {
            _logger.LogWarning("Cannot activate scene: not connected to bridge");
            return false;
        }

        try
        {
            var updateScene = new UpdateScene();
            updateScene.Actions.Add(new SceneAction { Action = new LightAction().TurnOn(), Target = new ResourceIdentifier() });
            await _client.UpdateSceneAsync(sceneId, updateScene);
            _logger.LogDebug("Activated scene {SceneId}", sceneId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate scene {SceneId}", sceneId);
            return false;
        }
    }

    // Additional methods for the Pairing page
    public async Task<List<DiscoveredBridge>> DiscoverBridgesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var bridges = await HueBridgeDiscovery.FastDiscoveryWithNetworkScanFallbackAsync(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

            return bridges.Select(b => new DiscoveredBridge
            {
                IpAddress = b.IpAddress,
                BridgeId = b.BridgeId
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering bridges");
            return new List<DiscoveredBridge>();
        }
    }

    public async Task<bool> PairWithBridgeAsync(string bridgeIp, CancellationToken cancellationToken = default)
    {
        try
        {
            // Create or update bridge status with the provided IP
            if (_bridgeStatus == null)
            {
                _bridgeStatus = new BridgeStatus();
            }
            _bridgeStatus.IpAddress = bridgeIp;
            
            var appKey = await PairWithBridgeAsync(cancellationToken);
            return !string.IsNullOrEmpty(appKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pairing with bridge at {BridgeIp}", bridgeIp);
            return false;
        }
    }

    public async Task<BridgeStatus?> GetBridgeStatusAsync()
    {
        try
        {
            var appKey = _config.CurrentValue.HueBridge.AppKey;
            var isPaired = !string.IsNullOrEmpty(appKey);
            
            // If no bridge status and no app key, return null (no bridge discovered)
            if (_bridgeStatus == null && !isPaired)
            {
                return null;
            }
            
            // If we have credentials but no bridge status, create one
            if (_bridgeStatus == null && isPaired)
            {
                _bridgeStatus = new BridgeStatus
                {
                    IsPaired = true,
                    AppKey = appKey,
                    IsConnected = false
                };
            }
            
            // Update pairing status from configuration
            if (_bridgeStatus != null)
            {
                _bridgeStatus.IsPaired = isPaired;
                _bridgeStatus.AppKey = appKey;
            }
            
            if (string.IsNullOrEmpty(_bridgeStatus?.IpAddress))
            {
                return _bridgeStatus; // Bridge status exists but no IP known
            }

            if (!isPaired)
            {
                // Bridge discovered but not paired
                return _bridgeStatus;
            }

            if (_client == null)
            {
                _client = new LocalHueApi(_bridgeStatus.IpAddress, appKey);
            }

            var bridge = await _client.GetBridgeAsync();
            var bridgeData = bridge?.Data?.FirstOrDefault();

            _bridgeStatus.IsConnected = true;
            _bridgeStatus.IsPaired = true;
            _bridgeStatus.BridgeId = bridgeData?.Id != Guid.Empty ? bridgeData?.Id.ToString() : _bridgeStatus.BridgeId ?? "Unknown";
            _bridgeStatus.ApiVersion = bridgeData?.Metadata?.Archetype ?? "Unknown";

            return _bridgeStatus;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bridge status");
            if (_bridgeStatus != null)
            {
                _bridgeStatus.IsConnected = false;
                // Keep pairing status based on configuration
                _bridgeStatus.IsPaired = !string.IsNullOrEmpty(_config.CurrentValue.HueBridge.AppKey);
                _bridgeStatus.AppKey = _config.CurrentValue.HueBridge.AppKey;
            }
            return _bridgeStatus;
        }
    }

    public async Task UnpairFromBridgeAsync()
    {
        try
        {
            // Clear the app key and bridge IP from configuration
            await _configUpdateService.UpdateHueBridgeConfigAsync(
                ipAddress: null,
                appKey: null,
                autoDiscover: true // Reset to auto-discover mode
            );
            
            _logger.LogInformation("Unpaired from Hue Bridge and cleared configuration");
            _client = null;
            
            if (_bridgeStatus != null)
            {
                _bridgeStatus.IsPaired = false;
                _bridgeStatus.AppKey = null;
                _bridgeStatus.IsConnected = false;
                _bridgeStatus.IpAddress = null; // Clear the IP address as well
                _bridgeStatus.BridgeId = null;
                _bridgeStatus.ApiVersion = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unpairing from bridge");
            throw;
        }
    }

    // String overloads for easier integration with mappings
    public async Task<bool> SetLightStateAsync(string lightId, bool on, byte? brightness = null, CancellationToken cancellationToken = default)
    {
        if (Guid.TryParse(lightId, out var guid))
        {
            return await SetLightStateAsync(guid, on, brightness, cancellationToken);
        }
        
        _logger.LogWarning("Invalid light ID format: {LightId}", lightId);
        return false;
    }

    public async Task<bool> SetLightColorAsync(string lightId, byte red, byte green, byte blue, byte? brightness = null, CancellationToken cancellationToken = default)
    {
        if (Guid.TryParse(lightId, out var guid))
        {
            return await SetLightColorAsync(guid, red, green, blue, brightness, cancellationToken);
        }
        
        _logger.LogWarning("Invalid light ID format: {LightId}", lightId);
        return false;
    }

    public async Task<bool> SetGroupStateAsync(string groupId, bool on, byte? brightness = null, CancellationToken cancellationToken = default)
    {
        if (Guid.TryParse(groupId, out var guid))
        {
            return await SetGroupStateAsync(guid, on, brightness, cancellationToken);
        }
        
        _logger.LogWarning("Invalid group ID format: {GroupId}", groupId);
        return false;
    }

    public async Task<bool> ActivateSceneAsync(string sceneId, CancellationToken cancellationToken = default)
    {
        if (Guid.TryParse(sceneId, out var guid))
        {
            return await ActivateSceneAsync(guid, cancellationToken);
        }
        
        _logger.LogWarning("Invalid scene ID format: {SceneId}", sceneId);
        return false;
    }
}

// Supporting classes for the Pairing page
public class DiscoveredBridge
{
    public string IpAddress { get; set; } = "";
    public string BridgeId { get; set; } = "";
}

public class BridgeStatus
{
    public bool IsConnected { get; set; }
    public bool IsPaired { get; set; }
    public string? IpAddress { get; set; }
    public string? BridgeId { get; set; }
    public string? ApiVersion { get; set; }
    public string? AppKey { get; set; }
}
