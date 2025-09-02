using LoxoneHueBridge.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LoxoneHueBridge.Core.Configuration;
using loxonelinkdotnet;
using loxonelinkdotnet.Devices.Extensions;
using loxonelinkdotnet.Devices.TreeDevices;
using loxonelinkdotnet.Can.Adapters;
using loxonelinkdotnet.Devices;

namespace LoxoneHueBridge.Core.Services;

public interface ILoxoneLinkService
{
    event EventHandler<NatFrame>? FrameReceived;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<TreeExtension?> CreateExtensionAsync(uint extensionSerial, CancellationToken cancellationToken = default);
    Task<RgbwTreeDevice?> AddDeviceToExtensionAsync(uint extensionSerial, uint deviceSerial, Guid hueId, CancellationToken cancellationToken = default);
    Task RemoveDeviceFromExtensionAsync(uint extensionSerial, uint deviceSerial, CancellationToken cancellationToken = default);
    bool IsRunning { get; }
}

public class ObservedLoxoneDevice
{
    public uint ExtensionSerial { get; set; }
    public uint DeviceSerial { get; set; }
    public string DeviceType { get; set; } = "";
    public DateTime LastSeen { get; set; }
    public string LastCommand { get; set; } = "";
    public string LastValue { get; set; } = "";
    public Guid? MappedHueId { get; set; }
}

public class LoxoneLinkService : ILoxoneLinkService, IDisposable
{
    private readonly ILogger<LoxoneLinkService> _logger;
    private readonly IOptionsMonitor<LoxoneHueBridgeConfig> _config;
    private readonly IMappingService _mappingService;
    private readonly IHueService _hueService;
    private ICanInterface? _canAdapter;
    private readonly Dictionary<uint, TreeExtension> _extensions = new();
    private readonly Dictionary<uint, Dictionary<uint, RgbwTreeDevice>> _devices = new(); // ExtensionSerial -> DeviceSerial -> Device
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _listenerTask;
    private bool _disposed;

    public event EventHandler<NatFrame>? FrameReceived;
    public bool IsRunning => _canAdapter != null;

    public LoxoneLinkService(
        ILogger<LoxoneLinkService> logger, 
        IOptionsMonitor<LoxoneHueBridgeConfig> config,
        IMappingService mappingService,
        IHueService hueService)
    {
        _logger = logger;
        _config = config;
        _mappingService = mappingService;
        _hueService = hueService;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            _logger.LogWarning("Loxone Link service is already running");
            return;
        }

        var config = _config.CurrentValue;
        
        if (config.MockMode)
        {
            _logger.LogInformation("Starting Loxone Link service in mock mode");
            await StartMockModeAsync(cancellationToken);
            return;
        }

        try
        {
            _logger.LogInformation("Starting Loxone Link service");
            
            // Initialize CAN adapter (use SocketCan for Linux, could be configurable)
            _canAdapter = new WaveshareSerialCan(config.CanInterface, 2000000, 125000, _logger);

            //await _canAdapter.StartReceiving(cancellationToken);
            _canAdapter.StartReceiving();

            _logger.LogInformation("Loxone Link service started successfully with CAN interface {CanInterface}", 
                config.CanInterface);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Loxone Link service");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning && _listenerTask == null)
        {
            return;
        }

        try
        {
            _cancellationTokenSource?.Cancel();
            
            if (_listenerTask != null)
            {
                await _listenerTask;
            }

            // Stop all extensions
            foreach (var extension in _extensions.Values)
            {
                try
                {
                    await extension.StopAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping extension {ExtensionSerial:X8}", extension.SerialNumber);
                }
            }
            
            _extensions.Clear();
            _devices.Clear();

            if (_canAdapter != null)
            {
                _canAdapter.StopReceiving();
                _canAdapter.Dispose();
                _canAdapter = null;
            }
            
            _logger.LogInformation("Loxone Link service stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping Loxone Link service");
        }
    }

    public async Task<TreeExtension?> CreateExtensionAsync(uint extensionSerial, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_canAdapter == null)
            {
                _logger.LogWarning("Cannot create extension - CAN adapter not initialized");
                return null;
            }

            if (_extensions.ContainsKey(extensionSerial))
            {
                _logger.LogInformation("Extension {ExtensionSerial:X8} already exists", extensionSerial);
                return _extensions[extensionSerial];
            }

            var extension = new TreeExtension(
                serialNumber: extensionSerial,
                hardwareVersion: 2,
                firmwareVersion: 13030124,
                devices: Array.Empty<RgbwTreeDevice>(),
                canBus: _canAdapter,
                logger: _logger);

            _extensions[extensionSerial] = extension;
            _devices[extensionSerial] = new Dictionary<uint, RgbwTreeDevice>();

            await extension.StartAsync();
            
            _logger.LogInformation("Created extension {ExtensionSerial:X8}", extensionSerial);
            return extension;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create extension {ExtensionSerial:X8}", extensionSerial);
            return null;
        }
    }

    public async Task<RgbwTreeDevice?> AddDeviceToExtensionAsync(uint extensionSerial, uint deviceSerial, Guid hueId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure extension exists
            if (!_extensions.ContainsKey(extensionSerial))
            {
                await CreateExtensionAsync(extensionSerial, cancellationToken);
            }

            var extension = _extensions[extensionSerial];
            var deviceDict = _devices[extensionSerial];

            // Check if device already exists
            if (deviceDict.ContainsKey(deviceSerial))
            {
                _logger.LogInformation("Device {DeviceSerial:X8} already exists in extension {ExtensionSerial:X8}", 
                    deviceSerial, extensionSerial);
                return deviceDict[deviceSerial];
            }

            // Check device limit (max 50 devices per extension)
            if (deviceDict.Count >= 50)
            {
                _logger.LogWarning("Extension {ExtensionSerial:X8} already has maximum number of devices (50)", extensionSerial);
                return null;
            }

            // Create RGBW device for Hue light control
            var device = new RgbwDimmerTreeDevice(deviceSerial, 1, 13030124, _logger);

            device.ValueChanged += async (s, e) =>
            {
                try
                {
                    // TODO: Handle white channel
                    await _hueService.SetLightColorAsync(hueId.ToString(), e.Red, e.Green, e.Blue, cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error setting Hue light color for device {DeviceSerial:X8}", deviceSerial);
                }
            };

            // Add device to extension
            extension.AddDevice(device, TreeBranches.Right); // Could be configurable
            await device.StartAsync();
            deviceDict[deviceSerial] = device;
            
            _logger.LogInformation("Added device {DeviceSerial:X8} to extension {ExtensionSerial:X8} mapped to Hue {HueId}", 
                deviceSerial, extensionSerial, hueId);
            
            return device;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add device {DeviceSerial:X8} to extension {ExtensionSerial:X8}", 
                deviceSerial, extensionSerial);
            return null;
        }
    }

    public async Task RemoveDeviceFromExtensionAsync(uint extensionSerial, uint deviceSerial, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_extensions.ContainsKey(extensionSerial) || !_devices.ContainsKey(extensionSerial))
            {
                _logger.LogWarning("Extension {ExtensionSerial:X8} does not exist", extensionSerial);
                return;
            }

            var extension = _extensions[extensionSerial];
            var deviceDict = _devices[extensionSerial];

            if (!deviceDict.ContainsKey(deviceSerial))
            {
                _logger.LogWarning("Device {DeviceSerial:X8} does not exist in extension {ExtensionSerial:X8}", 
                    deviceSerial, extensionSerial);
                return;
            }

            var device = deviceDict[deviceSerial];
            extension.RemoveDevice(device);
            deviceDict.Remove(deviceSerial);
            
            _logger.LogInformation("Removed device {DeviceSerial:X8} from extension {ExtensionSerial:X8}", 
                deviceSerial, extensionSerial);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove device {DeviceSerial:X8} from extension {ExtensionSerial:X8}", 
                deviceSerial, extensionSerial);
        }
    }

    private async Task StartMockModeAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _listenerTask = SimulateFramesAsync(_cancellationTokenSource.Token);
        
        _logger.LogInformation("Loxone Link service started in mock mode");
    }

    private async Task SimulateFramesAsync(CancellationToken cancellationToken)
    {
        var random = new Random();
        var mockExtensions = new[] { 0x12345678u, 0xAABBCCDDu, 0x87654321u };
        
        // Create mock extensions and devices
        foreach (var extSerial in mockExtensions)
        {
            for (uint deviceId = 1; deviceId <= 3; deviceId++)
            {
                var deviceSerial = extSerial + deviceId;
            }
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var extSerial = mockExtensions[random.Next(mockExtensions.Length)];
                var deviceSerial = extSerial + (uint)random.Next(1, 4);
                
                // Simulate different types of events
                var eventType = random.Next(3);
                NatFrame? frame = eventType switch
                {
                    0 => CreateMockDigitalFrame(deviceSerial, random.Next(2) == 1),
                    1 => CreateMockAnalogFrame(deviceSerial, random.NextDouble() * 100),
                    2 => CreateMockRgbwFrame(deviceSerial, 
                        (byte)random.Next(256), (byte)random.Next(256), 
                        (byte)random.Next(256), (byte)random.Next(256)),
                    _ => null
                };

                if (frame != null)
                {                    
                    FrameReceived?.Invoke(this, frame);
                    
                    _logger.LogDebug("Simulated Loxone frame: Extension {ExtSerial:X8}, Device {DeviceSerial:X8}, Type {CommandType}",
                        extSerial, frame.DeviceId, frame.CommandType);
                }

                await Task.Delay(5000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static NatFrame CreateMockDigitalFrame(uint deviceId, bool value)
    {
        var data = new byte[] { (byte)(value ? 1 : 0) };
        return new NatFrame(0x100 + deviceId, NatCommandType.DigitalOutput, (byte)deviceId, data);
    }

    private static NatFrame CreateMockAnalogFrame(uint deviceId, double value)
    {
        var rawValue = (uint)(value * 1000000);
        var data = BitConverter.GetBytes(rawValue);
        return new NatFrame(0x200 + deviceId, NatCommandType.AnalogOutput, (byte)deviceId, data);
    }

    private static NatFrame CreateMockRgbwFrame(uint deviceId, byte r, byte g, byte b, byte w)
    {
        var data = new byte[] { r, g, b, w };
        return new NatFrame(0x300 + deviceId, NatCommandType.RgbwOutput, (byte)deviceId, data);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopAsync().Wait(5000);
        _cancellationTokenSource?.Dispose();
        
        foreach (var extension in _extensions.Values)
        {
            try
            {
                extension.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing extension");
            }
        }
        
        _canAdapter?.Dispose();
        
        _disposed = true;
    }
}