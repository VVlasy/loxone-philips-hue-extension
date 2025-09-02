using LoxoneHueBridge.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace LoxoneHueBridge.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatusController : ControllerBase
{
    private readonly ILogger<StatusController> _logger;
    private readonly ICanListenerService _canListener;
    private readonly IHueService _hueService;
    private readonly IMappingService _mappingService;

    public StatusController(
        ILogger<StatusController> logger,
        ICanListenerService canListener,
        IHueService hueService,
        IMappingService mappingService)
    {
        _logger = logger;
        _canListener = canListener;
        _hueService = hueService;
        _mappingService = mappingService;
    }

    [HttpGet]
    public async Task<IActionResult> GetStatus()
    {
        var lights = await _hueService.GetLightsAsync();
        var mappings = _mappingService.GetAllMappings();

        return Ok(new
        {
            Timestamp = DateTime.UtcNow,
            CanBus = new
            {
                IsRunning = _canListener.IsRunning,
                Status = _canListener.IsRunning ? "Connected" : "Disconnected"
            },
            HueBridge = new
            {
                IsConnected = _hueService.IsConnected,
                BridgeIp = _hueService.BridgeIp,
                LightsCount = lights.Count(),
                Status = _hueService.IsConnected ? "Connected" : "Not Paired"
            },
            Mappings = new
            {
                Count = mappings.Count(),
                Items = mappings.Select(m => new
                {
                    m.NatDeviceId,
                    m.HueTargetId,
                    m.HueTargetType,
                    m.MappingType
                })
            }
        });
    }

    [HttpGet("can")]
    public IActionResult GetCanStatus()
    {
        return Ok(new
        {
            IsRunning = _canListener.IsRunning,
            Status = _canListener.IsRunning ? "Connected" : "Disconnected"
        });
    }

    [HttpGet("hue")]
    public async Task<IActionResult> GetHueStatus()
    {
        var isConnected = await _hueService.TestConnectionAsync();
        var lights = await _hueService.GetLightsAsync();

        return Ok(new
        {
            IsConnected = isConnected,
            BridgeIp = _hueService.BridgeIp,
            LightsCount = lights.Count(),
            Status = isConnected ? "Connected" : "Not Paired"
        });
    }
}

[ApiController]
[Route("api/[controller]")]
public class HueController : ControllerBase
{
    private readonly ILogger<HueController> _logger;
    private readonly IHueService _hueService;

    public HueController(ILogger<HueController> logger, IHueService hueService)
    {
        _logger = logger;
        _hueService = hueService;
    }

    [HttpPost("discover")]
    public async Task<IActionResult> DiscoverBridge()
    {
        var discovered = await _hueService.DiscoverBridgeAsync();
        return Ok(new { Success = discovered, BridgeIp = _hueService.BridgeIp });
    }

    [HttpPost("pair")]
    public async Task<IActionResult> PairBridge()
    {
        var appKey = await _hueService.PairWithBridgeAsync();
        return Ok(new { Success = !string.IsNullOrEmpty(appKey), AppKey = appKey });
    }

    [HttpGet("lights")]
    public async Task<IActionResult> GetLights()
    {
        var lights = await _hueService.GetLightsAsync();
        return Ok(lights.Select(l => new
        {
            l.Id,
            l.Metadata?.Name,
            l.On, //l.On?.IsOn ?? false,
            Brightness = l.Dimming?.Brightness,
            l.Type
        }));
    }

    [HttpGet("groups")]
    public async Task<IActionResult> GetGroups()
    {
        var groups = await _hueService.GetGroupsAsync();
        return Ok(groups.Select(g => new
        {
            g.Id,
            g.Metadata?.Name,
            g.On, //g.On?.IsOn ?? false,
            Brightness = g.Dimming?.Brightness,
            g.Type
        }));
    }

    [HttpGet("scenes")]
    public async Task<IActionResult> GetScenes()
    {
        var scenes = await _hueService.GetScenesAsync();
        return Ok(scenes.Select(s => new
        {
            s.Id,
            s.Metadata?.Name,
            s.Type
        }));
    }
}
