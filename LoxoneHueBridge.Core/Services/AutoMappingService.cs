using LoxoneHueBridge.Core.Models;
using LoxoneHueBridge.Core.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LoxoneHueBridge.Core.Configuration;

namespace LoxoneHueBridge.Core.Services;

public interface IAutoMappingService
{
    Task GenerateAutomaticMappingsAsync(CancellationToken cancellationToken = default);
    Task<HueMapping> CreateMappingForLightAsync(string extensionSerial, string deviceSerial, string lightId, string lightName, CancellationToken cancellationToken = default);
    Task RegisterDiscoveredDeviceAsync(uint extensionSerial, uint deviceSerial, CancellationToken cancellationToken = default);
}

public class AutoMappingService : IAutoMappingService
{
    private readonly ILogger<AutoMappingService> _logger;
    private readonly IMappingService _mappingService;
    private readonly IHueService _hueService;
    private readonly ILoxoneLinkService _loxoneLinkService;
    private readonly IOptionsMonitor<LoxoneHueBridgeConfig> _config;
    private readonly Dictionary<string, DateTime> _discoveredDevices = new();
    private readonly object _lock = new();
    private readonly SemaphoreSlim _generateMappingsSemaphore = new(1, 1);

    public AutoMappingService(
        ILogger<AutoMappingService> logger,
        IMappingService mappingService,
        IHueService hueService,
        ILoxoneLinkService loxoneLinkService,
        IOptionsMonitor<LoxoneHueBridgeConfig> config)
    {
        _logger = logger;
        _mappingService = mappingService;
        _hueService = hueService;
        _loxoneLinkService = loxoneLinkService;
        _config = config;
    }

    public async Task GenerateAutomaticMappingsAsync(CancellationToken cancellationToken = default)
    {
        // Prevent concurrent executions
        if (!await _generateMappingsSemaphore.WaitAsync(100, cancellationToken))
        {
            _logger.LogDebug("Automatic mapping generation already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Starting automatic mapping generation");

            // Check if Hue bridge is connected before proceeding
            var bridgeStatus = await _hueService.GetBridgeStatusAsync();
            if (bridgeStatus == null || !bridgeStatus.IsConnected || !bridgeStatus.IsPaired)
            {
                _logger.LogWarning("Skipping automatic mapping generation - Hue bridge is not connected or not paired. Connected: {Connected}, Paired: {Paired}",
                    bridgeStatus?.IsConnected ?? false, bridgeStatus?.IsPaired ?? false);
                return;
            }

            // Get all available Hue lights
            var lights = await _hueService.GetLightsAsync(cancellationToken);
            var lightArray = lights.ToArray();

            if (lightArray.Length == 0)
            {
                _logger.LogWarning("No Hue lights found - cannot generate automatic mappings");
                return;
            }

            // Check existing mappings first
            var existingMappings = await _mappingService.GetAllMappingsAsync();

            // Build lookup of mapped Hue resource IDs and used device IDs per extension
            var mappedHueIds = new HashSet<Guid>(existingMappings.Select(m => m.HueTargetId));
            var usedByExtension = existingMappings
                .GroupBy(m => m.ExtensionSerial)
                .ToDictionary(
                    g => g.Key,
                    g => new HashSet<uint>(g.Select(m => HexFormatHelper.FromHexString(m.DeviceSerial)))
                );
            var extensionCapacity = existingMappings
                .GroupBy(m => m.ExtensionSerial)
                .ToDictionary(g => g.Key, g => g.Count());

            // Local helpers to pick extension and next free device ID
            string PickOrCreateExtension()
            {
                var candidate = extensionCapacity
                    .Where(kvp => kvp.Value < 50)
                    .OrderBy(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(candidate))
                    return candidate!;

                var existingExts = new HashSet<string>(extensionCapacity.Keys);
                while (true)
                {
                    var newSerial = GenerateSampleExtensionSerials(1)[0];
                    var hex = HexFormatHelper.ToHexString(newSerial);
                    if (!existingExts.Contains(hex))
                    {
                        extensionCapacity[hex] = 0;
                        usedByExtension[hex] = new HashSet<uint>();
                        return hex;
                    }
                }
            }

            uint NextDeviceSerialForExtension(string extHex)
            {
                if (!usedByExtension.TryGetValue(extHex, out var used))
                {
                    used = new HashSet<uint>();
                    usedByExtension[extHex] = used;
                }

                uint candidate = 0xF0000001; // start range for generated devices
                while (used.Contains(candidate))
                {
                    candidate++;
                }
                used.Add(candidate);
                extensionCapacity[extHex] = (extensionCapacity.TryGetValue(extHex, out var count) ? count : 0) + 1;
                return candidate;
            }

            var newMappingsCount = 0;

            // Prepare lookup: Hue light -> existing mappings
            var mappingsByHueId = existingMappings
                .GroupBy(m => m.HueTargetId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Use lights as the source. Ensure devices exist for mapped lights; create for unmapped.
            foreach (var light in lightArray)
            {
                if (mappingsByHueId.TryGetValue(light.Id, out var existingForLight) && existingForLight.Count > 0)
                {
                    // Light already mapped: ensure Loxone device(s) exist and are started
                    foreach (var m in existingForLight)
                    {
                        var extSerial = HexFormatHelper.FromHexString(m.ExtensionSerial);
                        var devSerial = HexFormatHelper.FromHexString(m.DeviceSerial);
                        await _loxoneLinkService.AddDeviceToExtensionAsync(extSerial, devSerial, light.Id, cancellationToken);
                        _logger.LogDebug("Ensured device exists for mapped light {LightId}: Extension {ExtensionSerial}/Device {DeviceSerial}",
                            light.Id, m.ExtensionSerial, m.DeviceSerial);
                    }
                    continue;
                }

                // Unmapped light: create mapping and corresponding device
                var extHex = PickOrCreateExtension();
                var devSerialNew = NextDeviceSerialForExtension(extHex);
                var devHex = HexFormatHelper.ToHexString(devSerialNew);

                var mapping = await CreateMappingForLightAsync(
                    extHex,
                    devHex,
                    light.Id.ToString(),
                    light.Metadata?.Name ?? $"Light {light.Id}",
                    cancellationToken);

                await _mappingService.AddMappingAsync(mapping);

                await _loxoneLinkService.AddDeviceToExtensionAsync(
                    HexFormatHelper.FromHexString(extHex),
                    devSerialNew,
                    light.Id,
                    cancellationToken);

                newMappingsCount++;
                _logger.LogInformation(
                    "Created automatic mapping from light: Hue Light {LightId} ({LightName}) -> Extension {ExtensionSerial}/Device {DeviceSerial}",
                    light.Id,
                    light.Metadata?.Name,
                    extHex,
                    devHex);
            }

            // Save mappings if auto-save is enabled
            if (_config.CurrentValue.Mappings.AutoSave && newMappingsCount > 0)
            {
                await _mappingService.SaveMappingsAsync(_config.CurrentValue.Mappings.ConfigFile);
                _logger.LogInformation("Auto-saved {Count} new mappings to {ConfigFile}", 
                    newMappingsCount, _config.CurrentValue.Mappings.ConfigFile);
            }

            _logger.LogInformation("Automatic mapping generation completed. Created {Count} new mappings", newMappingsCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate automatic mappings");
            throw;
        }
        finally
        {
            _generateMappingsSemaphore.Release();
        }
    }

    public async Task<HueMapping> CreateMappingForLightAsync(string extensionSerial, string deviceSerial, string lightId, string lightName, CancellationToken cancellationToken = default)
    {
        // Determine the best mapping type based on light capabilities
        var lights = await _hueService.GetLightsAsync(cancellationToken);
        var light = lights.FirstOrDefault(l => l.Id.ToString() == lightId);
        string mappingType = "digital"; // Default
        
        if (light != null && light.Color != null)
        {
            // Light supports color - use RGBW mapping
            mappingType = "rgbw";
        }
        else if (light != null && light.Dimming != null)
        {
            // Light supports dimming - use analog mapping
            mappingType = "analog";
        }

        var mapping = new HueMapping(
            ExtensionSerial: extensionSerial,
            DeviceSerial: deviceSerial,
            HueTargetId: Guid.Parse(lightId),
            HueTargetType: "light",
            MappingType: mappingType,
            Options: new Dictionary<string, object>
            {
                { "LightName", lightName },
                { "AutoGenerated", true },
                { "CreatedAt", DateTime.UtcNow }
            }
        );

        return mapping;
    }

    public async Task RegisterDiscoveredDeviceAsync(uint extensionSerial, uint deviceSerial, CancellationToken cancellationToken = default)
    {
        var extHex = HexFormatHelper.ToHexString(extensionSerial);
        var devHex = HexFormatHelper.ToHexString(deviceSerial);
        var deviceKey = $"{extHex}-{devHex}";
        
        lock (_lock)
        {
            _discoveredDevices[deviceKey] = DateTime.Now;
        }

        // Check if Hue bridge is connected before proceeding with auto-mapping
        var bridgeStatus = await _hueService.GetBridgeStatusAsync();
        if (bridgeStatus == null || !bridgeStatus.IsConnected || !bridgeStatus.IsPaired)
        {
            _logger.LogDebug("Skipping auto-mapping for discovered device {ExtensionSerial}/{DeviceSerial} - Hue bridge is not connected or not paired",
                extHex, devHex);
            return;
        }

        // Check if we should automatically generate a mapping
        var existingMappings = await _mappingService.GetAllMappingsAsync();
        if (existingMappings.Any(m => m.ExtensionSerial == extHex && m.DeviceSerial == devHex))
        {
            _logger.LogDebug("Mapping already exists for discovered device {ExtensionSerial}/{DeviceSerial}",
                extHex, devHex);
            return;
        }

        // Try to create an automatic mapping
        var lights = await _hueService.GetLightsAsync(cancellationToken);
        var availableLight = lights.FirstOrDefault(light => 
            !existingMappings.Any(m => m.HueTargetId == light.Id));

        if (availableLight != null)
        {
            var mapping = await CreateMappingForLightAsync(
                extHex,
                devHex,
                availableLight.Id.ToString(), 
                availableLight.Metadata?.Name ?? $"Light {availableLight.Id}",
                cancellationToken);

            await _mappingService.AddMappingAsync(mapping);
            
            // Add the device to the Loxone extension
            await _loxoneLinkService.AddDeviceToExtensionAsync(
                extensionSerial,
                deviceSerial,
                availableLight.Id,
                cancellationToken);

            // Auto-save if enabled
            if (_config.CurrentValue.Mappings.AutoSave)
            {
                await _mappingService.SaveMappingsAsync(_config.CurrentValue.Mappings.ConfigFile);
            }

            _logger.LogInformation("Auto-created mapping for discovered device: Extension {ExtensionSerial}/Device {DeviceSerial} -> Hue Light {LightId} ({LightName})",
                extHex, devHex, availableLight.Id, availableLight.Metadata?.Name);
        }
        else
        {
            _logger.LogDebug("No available Hue lights for auto-mapping discovered device {ExtensionSerial}/{DeviceSerial}", 
                extHex, devHex);
        }
    }

    private async Task GenerateSampleMappingsAsync(HueApi.Models.Light[] lights, CancellationToken cancellationToken)
    {
        // Generate sample extension/device serials for demonstration
        var sampleExtensions = GenerateSampleExtensionSerials(Math.Min(lights.Length / 10, 3)); // Max 3 extensions
        
        var mappingsCount = 0;
        var lightIndex = 0;

        foreach (var extensionSerial in sampleExtensions)
        {
            // Add up to 10 devices per extension, or remaining lights
            var devicesInExtension = Math.Min(10, lights.Length - lightIndex);
            
            for (uint deviceId = 1; deviceId <= devicesInExtension && lightIndex < lights.Length; deviceId++)
            {
                var light = lights[lightIndex++];
                var deviceSerial = 0xF0000000 + deviceId;
                var extHex = HexFormatHelper.ToHexString(extensionSerial);
                var devHex = HexFormatHelper.ToHexString(deviceSerial);
                
                var mapping = await CreateMappingForLightAsync(
                    extHex,
                    devHex,
                    light.Id.ToString(), 
                    light.Metadata?.Name ?? $"Light {light.Id}",
                    cancellationToken);

                await _mappingService.AddMappingAsync(mapping);
                mappingsCount++;
                
                _logger.LogInformation("Created sample mapping: Extension {ExtensionSerial}/Device {DeviceSerial} -> Hue Light {LightId} ({LightName})",
                    extHex, devHex, light.Id, light.Metadata?.Name);
            }
        }

        // Save mappings if auto-save is enabled
        if (_config.CurrentValue.Mappings.AutoSave && mappingsCount > 0)
        {
            await _mappingService.SaveMappingsAsync(_config.CurrentValue.Mappings.ConfigFile);
            _logger.LogInformation("Auto-saved {Count} sample mappings to {ConfigFile}", 
                mappingsCount, _config.CurrentValue.Mappings.ConfigFile);
        }
    }

    private static uint[] GenerateSampleExtensionSerials(int count)
    {
        var random = new Random();
        var serials = new uint[count];
        
        for (int i = 0; i < count; i++)
        {
            // Generate realistic extension serial numbers
            serials[i] = (uint)(0x13000000 + (random.Next(0xFFFF) << 8) + i);
        }
        
        return serials;
    }
}
