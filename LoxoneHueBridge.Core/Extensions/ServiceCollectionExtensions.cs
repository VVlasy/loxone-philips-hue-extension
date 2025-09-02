using LoxoneHueBridge.Core.Configuration;
using LoxoneHueBridge.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LoxoneHueBridge.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLoxoneHueBridgeCore(this IServiceCollection services, IConfiguration configuration)
    {
        // Configuration
        services.Configure<LoxoneHueBridgeConfig>(configuration.GetSection("LoxoneHueBridge"));

        // Core Services
        services.AddSingleton<INatParser, NatParser>();
        services.AddSingleton<IMappingService, MappingService>();
        services.AddSingleton<IConfigurationUpdateService, ConfigurationUpdateService>();
        services.AddSingleton<IHueService, HueService>();
        services.AddSingleton<INatToHueMapper, NatToHueMapper>();
        
        // CAN Listener Service - configured based on settings
        services.AddSingleton<ICanListenerService>(serviceProvider =>
        {
            var config = serviceProvider.GetRequiredService<IOptionsMonitor<LoxoneHueBridgeConfig>>();
            var logger = serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CanListenerService>>();
            var interfaceName = config.CurrentValue.MockMode ? "mock" : config.CurrentValue.CanInterface;
            return new CanListenerService(logger, interfaceName);
        });

        // Background Services
        services.AddHostedService<CanBackgroundService>();
        services.AddHostedService<HueBackgroundService>();
        services.AddHostedService<HeartbeatService>();

        return services;
    }
}
