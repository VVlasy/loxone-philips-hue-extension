using LoxoneHueBridge.Core.Configuration;
using LoxoneHueBridge.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LoxoneHueBridge.Core.Services;

public class CanBackgroundService : BackgroundService
{
    private readonly ILogger<CanBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<LoxoneHueBridgeConfig> _config;

    public CanBackgroundService(
        ILogger<CanBackgroundService> logger,
        IServiceProvider serviceProvider,
        IOptionsMonitor<LoxoneHueBridgeConfig> config)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CAN Background Service starting...");

        using var scope = _serviceProvider.CreateScope();
        var canListener = scope.ServiceProvider.GetRequiredService<ICanListenerService>();
        var natParser = scope.ServiceProvider.GetRequiredService<INatParser>();
        var natToHueMapper = scope.ServiceProvider.GetRequiredService<INatToHueMapper>();
        var mappingService = scope.ServiceProvider.GetRequiredService<IMappingService>();

        // Load mappings
        var mappingsFile = _config.CurrentValue.Mappings.ConfigFile;
        await mappingService.LoadMappingsAsync(mappingsFile, stoppingToken);

        // Subscribe to CAN frames
        canListener.FrameReceived += async (sender, frame) =>
        {
            try
            {
                var natEvent = natParser.ParseFrame(frame);
                if (natEvent != null)
                {
                    await natToHueMapper.ProcessNatEventAsync(natEvent, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing CAN frame");
            }
        };

        try
        {
            // Start CAN listener
            var interfaceName = _config.CurrentValue.MockMode ? "mock" : _config.CurrentValue.CanInterface;
            await canListener.StartAsync(stoppingToken);

            _logger.LogInformation("CAN Background Service started successfully");

            // Keep service running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("CAN Background Service is stopping due to cancellation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CAN Background Service encountered an error");
            throw;
        }
        finally
        {
            await canListener.StopAsync(CancellationToken.None);
            
            // Save mappings if auto-save is enabled
            if (_config.CurrentValue.Mappings.AutoSave)
            {
                await mappingService.SaveMappingsAsync(mappingsFile, CancellationToken.None);
            }
            
            _logger.LogInformation("CAN Background Service stopped");
        }
    }
}

public class HueBackgroundService : BackgroundService
{
    private readonly ILogger<HueBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<LoxoneHueBridgeConfig> _config;

    public HueBackgroundService(
        ILogger<HueBackgroundService> logger,
        IServiceProvider serviceProvider,
        IOptionsMonitor<LoxoneHueBridgeConfig> config)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Hue Background Service starting...");

        using var scope = _serviceProvider.CreateScope();
        var hueService = scope.ServiceProvider.GetRequiredService<IHueService>();

        try
        {
            // Initial bridge discovery and connection
            await InitializeHueConnectionAsync(hueService, stoppingToken);

            // Periodic tasks
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
            
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
            {
                await PerformPeriodicTasksAsync(hueService, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Hue Background Service is stopping due to cancellation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hue Background Service encountered an error");
        }
        finally
        {
            _logger.LogInformation("Hue Background Service stopped");
        }
    }

    private async Task InitializeHueConnectionAsync(IHueService hueService, CancellationToken cancellationToken)
    {
        var config = _config.CurrentValue.HueBridge;

        // Test existing connection if app key is configured
        if (!string.IsNullOrEmpty(config.ManualIpAddress) && !string.IsNullOrEmpty(config.AppKey))
        {
            var connected = await hueService.TestConnectionAsync(cancellationToken);
            if (connected)
            {
                _logger.LogInformation("Successfully connected to Hue Bridge");
                return;
            }
            else
            {
                _logger.LogWarning("Failed to connect with existing app key. Re-pairing may be required.");
            }
        }

        _logger.LogInformation("Hue Bridge requires pairing. Use the Web UI to complete pairing process.");
    }

    private async Task PerformPeriodicTasksAsync(IHueService hueService, CancellationToken cancellationToken)
    {
        try
        {
            // Test connection periodically
            var connected = await hueService.TestConnectionAsync(cancellationToken);
            if (!connected)
            {
                _logger.LogWarning("Lost connection to Hue Bridge");
            }
            
            // Could add more periodic tasks here:
            // - Refresh light/scene lists
            // - Send heartbeat
            // - Check for bridge firmware updates
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during periodic Hue service tasks");
        }
    }
}

public class HeartbeatService : BackgroundService
{
    private readonly ILogger<HeartbeatService> _logger;

    public HeartbeatService(ILogger<HeartbeatService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Heartbeat Service starting...");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            _logger.LogDebug("LoxoneHueBridge is alive - {Timestamp}", DateTime.UtcNow);
        }

        _logger.LogInformation("Heartbeat Service stopped");
    }
}
