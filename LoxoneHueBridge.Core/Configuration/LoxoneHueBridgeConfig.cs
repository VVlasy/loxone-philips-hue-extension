namespace LoxoneHueBridge.Core.Configuration;

public class LoxoneHueBridgeConfig
{
    public string CanInterface { get; set; } = "can0";
    public int CanBitrate { get; set; } = 125000;
    public bool MockMode { get; set; } = false;
    public HueBridgeConfig HueBridge { get; set; } = new();
    public MappingsConfig Mappings { get; set; } = new();
}

public class HueBridgeConfig
{
    public bool AutoDiscover { get; set; } = true;
    public string? ManualIpAddress { get; set; }
    public string? AppKey { get; set; }
    public string ApplicationName { get; set; } = "LoxoneHueBridge";
    public string DeviceName { get; set; } = "LoxoneHueBridge";
}

public class MappingsConfig
{
    public string ConfigFile { get; set; } = "mappings.json";
    public bool AutoSave { get; set; } = true;
}
