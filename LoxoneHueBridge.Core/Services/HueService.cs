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
    Task<string?> PairWithBridgeAsync(CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Light>> GetLightsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<GroupedLight>> GetGroupsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<HueApi.Models.Scene>> GetScenesAsync(CancellationToken cancellationToken = default);
    Task<bool> SetLightStateAsync(Guid lightId, bool on, byte? brightness = null, CancellationToken cancellationToken = default);
    Task<bool> SetLightColorAsync(Guid lightId, byte red, byte green, byte blue, byte? brightness = null, CancellationToken cancellationToken = default);
    Task<bool> SetGroupStateAsync(Guid groupId, bool on, byte? brightness = null, CancellationToken cancellationToken = default);
    Task<bool> ActivateSceneAsync(Guid sceneId, CancellationToken cancellationToken = default);
    bool IsConnected { get; }
    string? BridgeIp { get; }
}

public class HueService : IHueService
{
    private readonly ILogger<HueService> _logger;
    private readonly IOptionsMonitor<LoxoneHueBridgeConfig> _config;
    private LocalHueApi? _client;
    private string? _bridgeIp;

    public bool IsConnected => _client != null && !string.IsNullOrEmpty(_config.CurrentValue.HueBridge.AppKey);
    public string? BridgeIp => _bridgeIp;

    public HueService(ILogger<HueService> logger, IOptionsMonitor<LoxoneHueBridgeConfig> config)
    {
        _logger = logger;
        _config = config;
    }

    public async Task<bool> DiscoverBridgeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var config = _config.CurrentValue.HueBridge;
            
            if (!config.AutoDiscover && !string.IsNullOrEmpty(config.ManualIpAddress))
            {
                _bridgeIp = config.ManualIpAddress;
                _logger.LogInformation("Using manual Hue Bridge IP: {BridgeIp}", _bridgeIp);
                return true;
            }

            _logger.LogInformation("Discovering Hue Bridge on network...");
            
            var locator = new HttpBridgeLocator();
            var bridges = await locator.LocateBridgesAsync(TimeSpan.FromSeconds(10));
            
            var bridge = bridges.FirstOrDefault();
            if (bridge == null)
            {
                _logger.LogWarning("No Hue Bridge found on network");
                return false;
            }

            _bridgeIp = bridge.IpAddress;
            _logger.LogInformation("Discovered Hue Bridge at IP: {BridgeIp}", _bridgeIp);
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
        if (string.IsNullOrEmpty(_bridgeIp))
        {
            _logger.LogWarning("Cannot pair: Bridge IP not set. Run discovery first.");
            return null;
        }

        try
        {
            var config = _config.CurrentValue.HueBridge;
            _logger.LogInformation("Attempting to pair with Hue Bridge at {BridgeIp}...", _bridgeIp);
            
            var result = await LocalHueApi.RegisterAsync(_bridgeIp, config.ApplicationName, config.DeviceName);
            
            if (result?.Username != null)
            {
                var appKey = result.Username;
                _logger.LogInformation("Successfully paired with Hue Bridge. App Key: {AppKey}", appKey);
                
                // Initialize client with new app key
                _client = new LocalHueApi(_bridgeIp, appKey);
                
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
                if (string.IsNullOrEmpty(_bridgeIp) || string.IsNullOrEmpty(appKey))
                {
                    return false;
                }
                
                _client = new LocalHueApi(_bridgeIp, appKey);
            }

            var config = await _client.GetBridgeAsync();
            _logger.LogDebug("Connection test successful. Bridge name: {BridgeName}", config?.Data?.FirstOrDefault()?.Metadata?.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection test failed");
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
            UpdateLight updateLight = null;
            
            if (brightness.HasValue && on)
            {
                updateLight = new UpdateLight().TurnOn().SetBrightness(brightness.Value / 254.0 * 100.0); // Convert from 0-254 to 0-100
            } else
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
            UpdateGroupedLight updateGroupedLight = null;
            
            if (brightness.HasValue && on)
            {
                updateGroupedLight = new UpdateGroupedLight().TurnOn().SetBrightness(brightness.Value / 254.0 * 100.0); // Convert from 0-254 to 0-100
            } else
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
}
