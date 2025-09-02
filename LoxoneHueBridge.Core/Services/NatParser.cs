using LoxoneHueBridge.Core.Models;
using Microsoft.Extensions.Logging;

namespace LoxoneHueBridge.Core.Services;

public interface INatParser
{
    NatEvent? ParseFrame(NatFrame frame);
}

public class NatParser : INatParser
{
    private readonly ILogger<NatParser> _logger;

    public NatParser(ILogger<NatParser> logger)
    {
        _logger = logger;
    }

    public NatEvent? ParseFrame(NatFrame frame)
    {
        var timestamp = DateTime.UtcNow;
        
        try
        {
            return frame.CommandType switch
            {
                NatCommandType.DigitalOutput or NatCommandType.DigitalInput => ParseDigitalFrame(frame, timestamp),
                NatCommandType.AnalogOutput or NatCommandType.AnalogInput => ParseAnalogFrame(frame, timestamp),
                NatCommandType.RgbwOutput or NatCommandType.RgbwInput => ParseRgbwFrame(frame, timestamp),
                _ => null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse NAT frame for device {DeviceId}, command type {CommandType}", 
                frame.DeviceId, frame.CommandType);
            return null;
        }
    }

    private DigitalChangedEvent? ParseDigitalFrame(NatFrame frame, DateTime timestamp)
    {
        if (frame.Data.Length < 1)
        {
            _logger.LogWarning("Digital frame for device {DeviceId} has insufficient data", frame.DeviceId);
            return null;
        }

        var value = frame.Data[0] != 0;
        _logger.LogDebug("Parsed digital event: Device {DeviceId}, Value {Value}", frame.DeviceId, value);
        
        return new DigitalChangedEvent(frame.DeviceId, timestamp, value);
    }

    private AnalogChangedEvent? ParseAnalogFrame(NatFrame frame, DateTime timestamp)
    {
        if (frame.Data.Length < 4)
        {
            _logger.LogWarning("Analog frame for device {DeviceId} has insufficient data", frame.DeviceId);
            return null;
        }

        // NAT protocol typically uses 32-bit values for analog data
        var rawValue = BitConverter.ToUInt32(frame.Data, 0);
        var value = rawValue / 1000000.0; // Convert from microunits to base units
        
        _logger.LogDebug("Parsed analog event: Device {DeviceId}, Value {Value}", frame.DeviceId, value);
        
        return new AnalogChangedEvent(frame.DeviceId, timestamp, value);
    }

    private RgbwChangedEvent? ParseRgbwFrame(NatFrame frame, DateTime timestamp)
    {
        if (frame.Data.Length < 4)
        {
            _logger.LogWarning("RGBW frame for device {DeviceId} has insufficient data", frame.DeviceId);
            return null;
        }

        var red = frame.Data[0];
        var green = frame.Data[1];
        var blue = frame.Data[2];
        var white = frame.Data.Length > 3 ? frame.Data[3] : (byte)0;
        
        _logger.LogDebug("Parsed RGBW event: Device {DeviceId}, R:{Red} G:{Green} B:{Blue} W:{White}", 
            frame.DeviceId, red, green, blue, white);
        
        return new RgbwChangedEvent(frame.DeviceId, timestamp, red, green, blue, white);
    }
}
