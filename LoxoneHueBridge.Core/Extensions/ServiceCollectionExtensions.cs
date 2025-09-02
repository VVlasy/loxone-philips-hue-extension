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
        services.AddSingleton<ConfigurationWriter>(serviceProvider =>
        {
            var config = serviceProvider.GetRequiredService<IConfiguration>();
            var environment = serviceProvider.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>();
            return new ConfigurationWriter(config, environment);
        });
        services.AddSingleton<INatParser, NatParser>();
        services.AddSingleton<IMappingService, MappingService>();
        services.AddSingleton<IConfigurationUpdateService, ConfigurationUpdateService>();
        services.AddSingleton<IHueService, HueService>();
        services.AddSingleton<INatToHueMapper, NatToHueMapper>();
        
        // Loxone Link Service - replaces CAN Listener Service
        services.AddSingleton<ILoxoneLinkService, LoxoneLinkService>();
        
        // Auto Mapping Service for generating automatic mappings
        services.AddSingleton<IAutoMappingService, AutoMappingService>();

        // Background Services
        services.AddHostedService<LoxoneLinkBackgroundService>();
        services.AddHostedService<HueBackgroundService>();
        services.AddHostedService<HeartbeatService>();

        return services;
    }
}
