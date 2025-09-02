using LoxoneHueBridge.Core.Configuration;

namespace LoxoneHueBridge.Core.Services;

public interface IConfigurationUpdateService
{
    Task UpdateHueBridgeConfigAsync(string? ipAddress = null, string? appKey = null, bool? autoDiscover = null);
    Task SaveConfigurationAsync();
}
