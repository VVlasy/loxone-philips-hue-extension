using LoxoneHueBridge.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace LoxoneHueBridge.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatusController : ControllerBase
{
    private readonly ILogger<StatusController> _logger;
    private readonly ILoxoneLinkService _loxoneLinkService;
    private readonly IHueService _hueService;
    private readonly IMappingService _mappingService;

    public StatusController(
        ILogger<StatusController> logger,
        ILoxoneLinkService loxoneLinkService,
        IHueService hueService,
        IMappingService mappingService)
    {
        _logger = logger;
        _loxoneLinkService = loxoneLinkService;
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
            LoxoneLink = new
            {
                IsRunning = _loxoneLinkService.IsRunning,
                Status = _loxoneLinkService.IsRunning ? "Connected" : "Disconnected"
            },
            HueBridge = new
            {
                IsConnected = _hueService.BridgeStatus?.IsConnected ?? false,
                IsPaired = _hueService.BridgeStatus?.IsPaired ?? false,
                BridgeIp = _hueService.BridgeStatus?.IpAddress,
                BridgeId = _hueService.BridgeStatus?.BridgeId,
                LightsCount = lights.Count(),
                Status = _hueService.BridgeStatus == null ? "Not Discovered" :
                         _hueService.BridgeStatus.IsConnected && _hueService.BridgeStatus.IsPaired ? "Connected" : 
                         _hueService.BridgeStatus.IsPaired ? "Paired (Offline)" : 
                         !string.IsNullOrEmpty(_hueService.BridgeStatus.IpAddress) ? "Discovered (Not Paired)" : "Bridge Found (No IP)"
            },
            Mappings = new
            {
                Count = mappings.Count(),
                Items = mappings.Select(m => new
                {
                    m.ExtensionSerial,
                    m.DeviceSerial,
                    m.HueTargetId,
                    m.HueTargetType,
                    m.MappingType
                })
            }
        });
    }

    [HttpGet("loxone")]
    public IActionResult GetLoxoneLinkStatus()
    {
        return Ok(new
        {
            IsRunning = _loxoneLinkService.IsRunning,
            Status = _loxoneLinkService.IsRunning ? "Connected" : "Disconnected"
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
            BridgeIp = _hueService.BridgeStatus?.IpAddress,
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
    private readonly IAutoMappingService _autoMappingService;

    public HueController(ILogger<HueController> logger, IHueService hueService, IAutoMappingService autoMappingService)
    {
        _logger = logger;
        _hueService = hueService;
        _autoMappingService = autoMappingService;
    }

    [HttpPost("discover")]
    public async Task<IActionResult> DiscoverBridge()
    {
        var discovered = await _hueService.DiscoverBridgeAsync();
        return Ok(new { Success = discovered, BridgeIp = _hueService.BridgeStatus?.IpAddress });
    }

    [HttpPost("pair")]
    public async Task<IActionResult> PairBridge()
    {
        var appKey = await _hueService.PairWithBridgeAsync();
        var success = !string.IsNullOrEmpty(appKey);

        // If pairing succeeded, trigger auto-mapping immediately
        if (success)
        {
            await _autoMappingService.GenerateAutomaticMappingsAsync();
        }

        return Ok(new { Success = success, AppKey = appKey });
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

    [HttpPost("clear-appkey")]
    public async Task<IActionResult> ClearAppKey()
    {
        try
        {
            await _hueService.UnpairFromBridgeAsync();
            return Ok(new { success = true, message = "App key cleared successfully" });
        }
        catch (Exception ex)
        {
            return Ok(new { success = false, message = ex.Message });
        }
    }
}
