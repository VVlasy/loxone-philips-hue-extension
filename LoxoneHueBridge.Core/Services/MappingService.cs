using LoxoneHueBridge.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace LoxoneHueBridge.Core.Services;

public interface IMappingService
{
    Task LoadMappingsAsync(string filePath, CancellationToken cancellationToken = default);
    Task SaveMappingsAsync(string filePath, CancellationToken cancellationToken = default);
    void AddMapping(HueMapping mapping);
    void RemoveMapping(uint natDeviceId);
    HueMapping? GetMapping(uint natDeviceId);
    IEnumerable<HueMapping> GetAllMappings();
    void ClearMappings();
}

public class MappingService : IMappingService
{
    private readonly ILogger<MappingService> _logger;
    private readonly Dictionary<uint, HueMapping> _mappings = new();
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
                        _mappings[mapping.NatDeviceId] = mapping;
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
        lock (_lock)
        {
            _mappings[mapping.NatDeviceId] = mapping;
        }
        
        _logger.LogInformation("Added mapping: NAT Device {NatDeviceId} -> Hue {HueTargetType} {HueTargetId} ({MappingType})",
            mapping.NatDeviceId, mapping.HueTargetType, mapping.HueTargetId, mapping.MappingType);
    }

    public void RemoveMapping(uint natDeviceId)
    {
        lock (_lock)
        {
            if (_mappings.Remove(natDeviceId))
            {
                _logger.LogInformation("Removed mapping for NAT Device {NatDeviceId}", natDeviceId);
            }
        }
    }

    public HueMapping? GetMapping(uint natDeviceId)
    {
        lock (_lock)
        {
            return _mappings.TryGetValue(natDeviceId, out var mapping) ? mapping : null;
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
        var mapping = _mappingService.GetMapping(natEvent.DeviceId);
        if (mapping == null)
        {
            _logger.LogDebug("No mapping found for NAT device {DeviceId}, using default behavior", natEvent.DeviceId);
            await ProcessWithDefaultMappingAsync(natEvent, cancellationToken);
            return;
        }

        try
        {
            await ProcessWithMappingAsync(natEvent, mapping, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process NAT event for device {DeviceId} with mapping", natEvent.DeviceId);
        }
    }

    private async Task ProcessWithMappingAsync(NatEvent natEvent, HueMapping mapping, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing NAT event for device {DeviceId} with mapping to {HueTargetType} {HueTargetId}",
            natEvent.DeviceId, mapping.HueTargetType, mapping.HueTargetId);

        switch (natEvent)
        {
            case DigitalChangedEvent digitalEvent:
                await ProcessDigitalEventAsync(digitalEvent, mapping, cancellationToken);
                break;
                
            case AnalogChangedEvent analogEvent:
                await ProcessAnalogEventAsync(analogEvent, mapping, cancellationToken);
                break;
                
            case RgbwChangedEvent rgbwEvent:
                await ProcessRgbwEventAsync(rgbwEvent, mapping, cancellationToken);
                break;
                
            default:
                _logger.LogWarning("Unknown NAT event type: {EventType}", natEvent.GetType().Name);
                break;
        }
    }

    private async Task ProcessDigitalEventAsync(DigitalChangedEvent digitalEvent, HueMapping mapping, CancellationToken cancellationToken)
    {
        switch (mapping.HueTargetType.ToLowerInvariant())
        {
            case "light":
                await _hueService.SetLightStateAsync(mapping.HueTargetId, digitalEvent.Value, cancellationToken: cancellationToken);
                break;
                
            case "group":
                await _hueService.SetGroupStateAsync(mapping.HueTargetId, digitalEvent.Value, cancellationToken: cancellationToken);
                break;
                
            case "scene":
                if (digitalEvent.Value) // Only activate scene on positive edge
                {
                    await _hueService.ActivateSceneAsync(mapping.HueTargetId, cancellationToken);
                }
                break;
                
            default:
                _logger.LogWarning("Unsupported Hue target type for digital event: {HueTargetType}", mapping.HueTargetType);
                break;
        }
    }

    private async Task ProcessAnalogEventAsync(AnalogChangedEvent analogEvent, HueMapping mapping, CancellationToken cancellationToken)
    {
        // Convert analog value (0-100) to brightness (0-254)
        var brightness = (byte)Math.Max(0, Math.Min(254, analogEvent.Value * 2.54));
        
        switch (mapping.HueTargetType.ToLowerInvariant())
        {
            case "light":
                await _hueService.SetLightStateAsync(mapping.HueTargetId, brightness > 0, brightness, cancellationToken);
                break;
                
            case "group":
                await _hueService.SetGroupStateAsync(mapping.HueTargetId, brightness > 0, brightness, cancellationToken);
                break;
                
            default:
                _logger.LogWarning("Unsupported Hue target type for analog event: {HueTargetType}", mapping.HueTargetType);
                break;
        }
    }

    private async Task ProcessRgbwEventAsync(RgbwChangedEvent rgbwEvent, HueMapping mapping, CancellationToken cancellationToken)
    {
        switch (mapping.HueTargetType.ToLowerInvariant())
        {
            case "light":
                await _hueService.SetLightColorAsync(mapping.HueTargetId, rgbwEvent.Red, rgbwEvent.Green, rgbwEvent.Blue, cancellationToken: cancellationToken);
                break;
                
            default:
                _logger.LogWarning("Unsupported Hue target type for RGBW event: {HueTargetType}", mapping.HueTargetType);
                break;
        }
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
