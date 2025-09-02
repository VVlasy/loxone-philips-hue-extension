namespace LoxoneHueBridge.Core.Configuration;

public class LoxoneHueBridgeConfig
{
    public string CanInterface { get; set; } = "can0";
    public int CanBitrate { get; set; } = 125000;
    public bool MockMode { get; set; } = false;
    public LoxoneLinkConfig LoxoneLink { get; set; } = new();
    public HueBridgeConfig HueBridge { get; set; } = new();
    public MappingsConfig Mappings { get; set; } = new();
    
    // Logging configuration
    public string LogLevel { get; set; } = "Information";
    public int LogRetentionDays { get; set; } = 30;
    public bool EnableFileLogging { get; set; } = true;
}

public class LoxoneLinkConfig
{
    public string? LoxoneServerUrl { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool AutoDiscoverDevices { get; set; } = true;
    public int AutoMappingIntervalMinutes { get; set; } = 10;
    public bool EnableAutoMapping { get; set; } = true;
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
