using LoxoneHueBridge.Core.Models;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace LoxoneHueBridge.Core.Services;

public interface ICanListenerService
{
    event EventHandler<NatFrame>? FrameReceived;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    bool IsRunning { get; }
}

// Simple CAN frame structure for mock implementation
public record CanFrame(uint CanId, byte[] Data);

public class CanListenerService : ICanListenerService, IDisposable
{
    private readonly ILogger<CanListenerService> _logger;
    private readonly string _interfaceName;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _listenerTask;
    private bool _disposed;

    public event EventHandler<NatFrame>? FrameReceived;
    public bool IsRunning => _listenerTask?.IsCompleted == false;

    public CanListenerService(ILogger<CanListenerService> logger, string interfaceName = "can0")
    {
        _logger = logger;
        _interfaceName = interfaceName;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            _logger.LogWarning("CAN listener is already running");
            return;
        }

        // For now, we'll use mock mode on all platforms
        // In a real implementation, you would check for Linux and use actual SocketCAN
        _logger.LogInformation("Starting CAN listener in mock mode (SocketCAN integration pending)");
        await StartMockModeAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            return;
        }

        _cancellationTokenSource?.Cancel();
        
        if (_listenerTask != null)
        {
            await _listenerTask;
        }
        
        _logger.LogInformation("CAN listener stopped");
    }

    private async Task StartMockModeAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _listenerTask = SimulateFramesAsync(_cancellationTokenSource.Token);
        
        _logger.LogInformation("CAN listener started in mock mode");
    }

    private async Task SimulateFramesAsync(CancellationToken cancellationToken)
    {
        var random = new Random();
        var deviceId = 1u;
        
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Simulate different types of NAT frames
                var frameType = random.Next(3);
                NatFrame? frame = frameType switch
                {
                    0 => CreateMockDigitalFrame(deviceId, random.Next(2) == 1),
                    1 => CreateMockAnalogFrame(deviceId + 1, random.NextDouble() * 100),
                    2 => CreateMockRgbwFrame(deviceId + 2, 
                        (byte)random.Next(256), (byte)random.Next(256), 
                        (byte)random.Next(256), (byte)random.Next(256)),
                    _ => null
                };

                if (frame != null)
                {
                    FrameReceived?.Invoke(this, frame);
                    _logger.LogDebug("Simulated NAT frame: Device {DeviceId}, Type {CommandType}", 
                        frame.DeviceId, frame.CommandType);
                }

                await Task.Delay(5000, cancellationToken); // Send frame every 5 seconds
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
        var rawValue = (uint)(value * 1000000); // Convert to microunits
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
        
        _disposed = true;
    }
}
