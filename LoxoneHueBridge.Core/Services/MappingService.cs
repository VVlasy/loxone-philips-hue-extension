using LoxoneHueBridge.Core.Models;
using LoxoneHueBridge.Core.Helpers;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace LoxoneHueBridge.Core.Services;

public interface IMappingService
{
    Task LoadMappingsAsync(string filePath, CancellationToken cancellationToken = default);
    Task SaveMappingsAsync(string filePath, CancellationToken cancellationToken = default);
    void AddMapping(HueMapping mapping);
    Task AddMappingAsync(HueMapping mapping);
    void RemoveMapping(string extensionSerial, string deviceSerial);
    Task RemoveMappingAsync(string extensionSerial, string deviceSerial);
    HueMapping? GetMapping(string extensionSerial, string deviceSerial);
    Task<HueMapping?> GetMappingAsync(string extensionSerial, string deviceSerial);
    IEnumerable<HueMapping> GetAllMappings();
    Task<List<HueMapping>> GetAllMappingsAsync();
    void ClearMappings();
}

public class MappingService : IMappingService
{
    private readonly ILogger<MappingService> _logger;
    private readonly Dictionary<string, HueMapping> _mappings = new();
    private readonly object _lock = new();

    public MappingService(ILogger<MappingService> logger)
    {
        _logger = logger;
    }

    public async Task LoadMappingsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                _logger.LogInformation("Mappings file {FilePath} not found, starting with empty mappings", filePath);
                return;
            }

            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var mappings = JsonSerializer.Deserialize<HueMapping[]>(json);

            lock (_lock)
            {
                _mappings.Clear();
                if (mappings != null)
                {
                    foreach (var mapping in mappings)
                    {
                        var key = $"{mapping.ExtensionSerial}-{mapping.DeviceSerial}";
                        _mappings[key] = mapping;
                    }
                }
            }

            _logger.LogInformation("Loaded {Count} mappings from {FilePath}", _mappings.Count, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load mappings from {FilePath}", filePath);
        }
    }

    public async Task SaveMappingsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            HueMapping[] mappingsArray;
            lock (_lock)
            {
                mappingsArray = _mappings.Values.ToArray();
            }

            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true 
            };
            var json = JsonSerializer.Serialize(mappingsArray, options);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(filePath, json, cancellationToken);
            _logger.LogInformation("Saved {Count} mappings to {FilePath}", mappingsArray.Length, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save mappings to {FilePath}", filePath);
        }
    }

    public void AddMapping(HueMapping mapping)
    {
        var key = $"{mapping.ExtensionSerial}-{mapping.DeviceSerial}";
        lock (_lock)
        {
            _mappings[key] = mapping;
        }
        
        _logger.LogInformation("Added mapping: Extension {ExtensionSerial}/Device {DeviceSerial} -> Hue {HueTargetType} {HueTargetId} ({MappingType})",
            mapping.ExtensionSerial, mapping.DeviceSerial, mapping.HueTargetType, mapping.HueTargetId, mapping.MappingType);
    }

    public void RemoveMapping(string extensionSerial, string deviceSerial)
    {
        var key = $"{extensionSerial}-{deviceSerial}";
        lock (_lock)
        {
            if (_mappings.Remove(key))
            {
                _logger.LogInformation("Removed mapping for Extension {ExtensionSerial}/Device {DeviceSerial}", 
                    extensionSerial, deviceSerial);
            }
        }
    }

    public HueMapping? GetMapping(string extensionSerial, string deviceSerial)
    {
        var key = $"{extensionSerial}-{deviceSerial}";
        lock (_lock)
        {
            return _mappings.TryGetValue(key, out var mapping) ? mapping : null;
        }
    }

    public IEnumerable<HueMapping> GetAllMappings()
    {
        lock (_lock)
        {
            return _mappings.Values.ToArray();
        }
    }

    public void ClearMappings()
    {
        lock (_lock)
        {
            _mappings.Clear();
        }
        
        _logger.LogInformation("Cleared all mappings");
    }

    // Additional async methods for consistency
    public async Task AddMappingAsync(HueMapping mapping)
    {
        await Task.CompletedTask; // Make async for consistency
        AddMapping(mapping);
    }

    public async Task RemoveMappingAsync(string extensionSerial, string deviceSerial)
    {
        await Task.CompletedTask; // Make async for consistency
        RemoveMapping(extensionSerial, deviceSerial);
    }

    public async Task<HueMapping?> GetMappingAsync(string extensionSerial, string deviceSerial)
    {
        await Task.CompletedTask; // Make async for consistency
        return GetMapping(extensionSerial, deviceSerial);
    }

    public async Task<List<HueMapping>> GetAllMappingsAsync()
    {
        await Task.CompletedTask; // Make async for consistency
        
        lock (_lock)
        {
            return _mappings.Values.ToList();
        }
    }
}

public interface INatToHueMapper
{
    Task ProcessNatEventAsync(NatEvent natEvent, CancellationToken cancellationToken = default);
}

public class NatToHueMapper : INatToHueMapper
{
    private readonly ILogger<NatToHueMapper> _logger;
    private readonly IMappingService _mappingService;
    private readonly IHueService _hueService;

    public NatToHueMapper(
        ILogger<NatToHueMapper> logger,
        IMappingService mappingService,
        IHueService hueService)
    {
        _logger = logger;
        _mappingService = mappingService;
        _hueService = hueService;
    }

    public async Task ProcessNatEventAsync(NatEvent natEvent, CancellationToken cancellationToken = default)
    {
        // For now, we'll use the default mapping approach since we don't have a direct
        // way to map from NatEvent.DeviceId to extension/device serials
        // This will be enhanced when we have proper device registration
        _logger.LogDebug("Processing NAT event for device {DeviceId} using default mapping", natEvent.DeviceId);
        await ProcessWithDefaultMappingAsync(natEvent, cancellationToken);
    }


    private async Task ProcessWithDefaultMappingAsync(NatEvent natEvent, CancellationToken cancellationToken)
    {
        // Default behavior: map to first available light with device ID as offset
        var lights = await _hueService.GetLightsAsync(cancellationToken);
        var lightArray = lights.ToArray();
        
        if (lightArray.Length == 0)
        {
            _logger.LogWarning("No lights available for default mapping");
            return;
        }

        var lightIndex = (int)(natEvent.DeviceId % lightArray.Length);
        var targetLight = lightArray[lightIndex];
        
        _logger.LogInformation("Using default mapping: NAT device {DeviceId} -> Light {LightId} ({LightName})",
            natEvent.DeviceId, targetLight.Id, targetLight.Metadata?.Name ?? targetLight.Id.ToString());

        switch (natEvent)
        {
            case DigitalChangedEvent digitalEvent:
                await _hueService.SetLightStateAsync(targetLight.Id!, digitalEvent.Value, cancellationToken: cancellationToken);
                break;
                
            case AnalogChangedEvent analogEvent:
                var brightness = (byte)Math.Max(0, Math.Min(254, analogEvent.Value * 2.54));
                await _hueService.SetLightStateAsync(targetLight.Id!, brightness > 0, brightness, cancellationToken);
                break;
                
            case RgbwChangedEvent rgbwEvent:
                await _hueService.SetLightColorAsync(targetLight.Id!, rgbwEvent.Red, rgbwEvent.Green, rgbwEvent.Blue, cancellationToken: cancellationToken);
                break;
        }
    }
}
