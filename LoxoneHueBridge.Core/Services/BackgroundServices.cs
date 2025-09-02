using LoxoneHueBridge.Core.Configuration;
using LoxoneHueBridge.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LoxoneHueBridge.Core.Services;

public class LoxoneLinkBackgroundService : BackgroundService
{
    private readonly ILogger<LoxoneLinkBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<LoxoneHueBridgeConfig> _config;

    public LoxoneLinkBackgroundService(
        ILogger<LoxoneLinkBackgroundService> logger,
        IServiceProvider serviceProvider,
        IOptionsMonitor<LoxoneHueBridgeConfig> config)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Loxone Link Background Service starting...");

        using var scope = _serviceProvider.CreateScope();
        var loxoneLinkService = scope.ServiceProvider.GetRequiredService<ILoxoneLinkService>();
        var natParser = scope.ServiceProvider.GetRequiredService<INatParser>();
        var natToHueMapper = scope.ServiceProvider.GetRequiredService<INatToHueMapper>();
        var mappingService = scope.ServiceProvider.GetRequiredService<IMappingService>();
        var autoMappingService = scope.ServiceProvider.GetRequiredService<IAutoMappingService>();

        // Load existing mappings
        var mappingsFile = _config.CurrentValue.Mappings.ConfigFile;
        await mappingService.LoadMappingsAsync(mappingsFile, stoppingToken);

        // Subscribe to Loxone frames
        loxoneLinkService.FrameReceived += async (sender, frame) =>
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
                _logger.LogError(ex, "Error processing Loxone frame");
            }
        };

        try
        {
            // Start Loxone Link service
            await loxoneLinkService.StartAsync(stoppingToken);

            _logger.LogInformation("Loxone Link Background Service started successfully");

            // Generate automatic mappings periodically
            using var mappingTimer = new PeriodicTimer(TimeSpan.FromMinutes(10));

            // Keep service running and periodically check for new devices to map
            while (!stoppingToken.IsCancellationRequested && await mappingTimer.WaitForNextTickAsync(stoppingToken))
            {
                // No periodics here
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Loxone Link Background Service is stopping due to cancellation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loxone Link Background Service encountered an error");
            throw;
        }
        finally
        {
            await loxoneLinkService.StopAsync(CancellationToken.None);
            
            // Save mappings if auto-save is enabled
            if (_config.CurrentValue.Mappings.AutoSave)
            {
                await mappingService.SaveMappingsAsync(mappingsFile, CancellationToken.None);
            }
            
            _logger.LogInformation("Loxone Link Background Service stopped");
        }
    }
}

public class HueBackgroundService : BackgroundService
{
    private readonly ILogger<HueBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<LoxoneHueBridgeConfig> _config;
    private bool _wasConnectedAndPaired = false;

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
                _wasConnectedAndPaired = true;

                // Trigger auto-mapping immediately on successful connection
                using (var scope = _serviceProvider.CreateScope())
                {
                    var autoMappingService = scope.ServiceProvider.GetRequiredService<IAutoMappingService>();
                    await autoMappingService.GenerateAutomaticMappingsAsync(cancellationToken);
                }

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
                if (_wasConnectedAndPaired)
                {
                    _wasConnectedAndPaired = false;
                }
            }
            else if (!_wasConnectedAndPaired)
            {
                // Transitioned to connected+paired: trigger auto-mapping once
                _wasConnectedAndPaired = true;
                using var scope = _serviceProvider.CreateScope();
                var autoMappingService = scope.ServiceProvider.GetRequiredService<IAutoMappingService>();
                await autoMappingService.GenerateAutomaticMappingsAsync(cancellationToken);
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
