namespace LoxoneHueBridge.Core.Models;

public enum NatCommandType : byte
{
    DigitalOutput = 0x80,
    DigitalInput = 0x81,
    AnalogOutput = 0x84,
    AnalogInput = 0x88,
    RgbwOutput = 0x8C,
    RgbwInput = 0x8D
}

public record NatFrame(
    uint CanId,
    NatCommandType CommandType,
    byte DeviceId,
    byte[] Data
);

public abstract record NatEvent(uint DeviceId, DateTime Timestamp);

public record DigitalChangedEvent(
    uint DeviceId, 
    DateTime Timestamp, 
    bool Value
) : NatEvent(DeviceId, Timestamp);

public record AnalogChangedEvent(
    uint DeviceId, 
    DateTime Timestamp, 
    double Value
) : NatEvent(DeviceId, Timestamp);

public record RgbwChangedEvent(
    uint DeviceId, 
    DateTime Timestamp, 
    byte Red, 
    byte Green, 
    byte Blue, 
    byte White
) : NatEvent(DeviceId, Timestamp);

public record HueMapping(
    string ExtensionSerial, // Format: "12:34:56:78"
    string DeviceSerial, // Format: "12:34:56:79"
    Guid HueTargetId,
    string HueTargetType, // "light", "group", "scene"
    string MappingType, // "digital", "analog", "rgbw"
    Dictionary<string, object>? Options = null
);
