using LoxoneHueBridge.Core.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LoxoneHueBridge.Web.Pages;

public class IndexModel : PageModel
{
    private readonly ILogger<IndexModel> _logger;
    private readonly ICanListenerService _canListener;
    private readonly IHueService _hueService;
    private readonly IMappingService _mappingService;

    public IndexModel(
        ILogger<IndexModel> logger, 
        ICanListenerService canListener,
        IHueService hueService,
        IMappingService mappingService)
    {
        _logger = logger;
        _canListener = canListener;
        _hueService = hueService;
        _mappingService = mappingService;
    }

    // Properties for initial data
    public bool CanIsRunning => _canListener.IsRunning;
    public string CanStatus => _canListener.IsRunning ? "Connected (Mock Mode)" : "Disconnected";
    public bool HueIsConnected => _hueService.BridgeStatus?.IsConnected ?? false;
    public bool HueIsPaired => _hueService.BridgeStatus?.IsPaired ?? false;
    public string? HueBridgeIp => _hueService.BridgeStatus?.IpAddress;
    public string HueStatus => _hueService.BridgeStatus == null ? "Not Discovered" :
                               _hueService.BridgeStatus.IsConnected && _hueService.BridgeStatus.IsPaired ? "Connected" : 
                               _hueService.BridgeStatus.IsPaired ? "Paired (Offline)" : 
                               !string.IsNullOrEmpty(_hueService.BridgeStatus.IpAddress) ? "Discovered (Not Paired)" : "Bridge Found (No IP)";

    public async Task OnGetAsync()
    {
        _logger.LogInformation("Dashboard page accessed");
        
        // Log initial status for debugging
        _logger.LogInformation("CAN Status: {CanStatus}, Hue Status: {HueStatus}", CanStatus, HueStatus);
    }
}
