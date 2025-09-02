using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using LoxoneHueBridge.Core.Services;
using HueApi.Models;

namespace LoxoneHueBridge.Web.Pages
{
    public class PairingModel : PageModel
    {
        private readonly IHueService _hueService;
        private readonly ILogger<PairingModel> _logger;

        public PairingModel(IHueService hueService, ILogger<PairingModel> logger)
        {
            _hueService = hueService;
            _logger = logger;
        }

        public BridgeStatus? BridgeStatus { get; set; }
        public DiscoveredBridge? DiscoveredBridge { get; set; }
        public IEnumerable<Light>? AvailableLights { get; set; }
        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }
        public bool IsProcessing { get; set; }
        public string ProcessingMessage { get; set; } = "";

        public async Task OnGetAsync()
        {
            await LoadBridgeStatus();
        }

        public async Task<IActionResult> OnPostDiscoverAsync()
        {
            try
            {
                IsProcessing = true;
                ProcessingMessage = "Discovering Hue Bridge on network...";
                
                _logger.LogInformation("Starting Hue Bridge discovery");
                var bridges = await _hueService.DiscoverBridgesAsync();
                
                if (bridges.Any())
                {
                    DiscoveredBridge = bridges.First();
                    SuccessMessage = $"Found Hue Bridge at {DiscoveredBridge.IpAddress}. Press the button on your bridge and click Pair.";
                    _logger.LogInformation("Discovered bridge at {IpAddress}", DiscoveredBridge.IpAddress);
                }
                else
                {
                    ErrorMessage = "No Hue Bridge found on the network. Please ensure your bridge is connected and try manual setup.";
                    _logger.LogWarning("No Hue Bridge discovered on network");
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error during discovery: {ex.Message}";
                _logger.LogError(ex, "Error discovering Hue Bridge");
            }
            finally
            {
                IsProcessing = false;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostPairAsync(string bridgeIp)
        {
            try
            {
                IsProcessing = true;
                ProcessingMessage = "Attempting to pair with Hue Bridge...";
                
                _logger.LogInformation("Attempting to pair with bridge at {BridgeIp}", bridgeIp);
                
                var success = await _hueService.PairWithBridgeAsync(bridgeIp);
                
                if (success)
                {
                    SuccessMessage = "Successfully paired with Hue Bridge! You can now control your lights from Loxone.";
                    _logger.LogInformation("Successfully paired with Hue Bridge at {BridgeIp}", bridgeIp);
                    await LoadBridgeStatus();
                }
                else
                {
                    ErrorMessage = "Pairing failed. Make sure you pressed the button on the bridge and try again.";
                    _logger.LogWarning("Failed to pair with Hue Bridge at {BridgeIp}", bridgeIp);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error during pairing: {ex.Message}";
                _logger.LogError(ex, "Error pairing with Hue Bridge");
            }
            finally
            {
                IsProcessing = false;
            }

            return Page();
        }

        public async Task<IActionResult> OnPostManualSetupAsync(string bridgeIp)
        {
            try
            {
                // Validate IP address format
                if (!System.Net.IPAddress.TryParse(bridgeIp, out _))
                {
                    ErrorMessage = "Invalid IP address format.";
                    return Page();
                }

                // Set discovered bridge manually for pairing
                DiscoveredBridge = new DiscoveredBridge 
                { 
                    IpAddress = bridgeIp, 
                    BridgeId = "manual-setup" 
                };
                
                SuccessMessage = $"Bridge IP set to {bridgeIp}. Press the button on your bridge and click Pair.";
                _logger.LogInformation("Manual bridge setup with IP {BridgeIp}", bridgeIp);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error in manual setup: {ex.Message}";
                _logger.LogError(ex, "Error in manual bridge setup");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostUnpairAsync()
        {
            try
            {
                _logger.LogInformation("Unpairing from Hue Bridge");
                await _hueService.UnpairFromBridgeAsync();
                SuccessMessage = "Successfully unpaired from Hue Bridge.";
                await LoadBridgeStatus();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error during unpairing: {ex.Message}";
                _logger.LogError(ex, "Error unpairing from Hue Bridge");
            }

            return Page();
        }

        private async Task LoadBridgeStatus()
        {
            try
            {
                BridgeStatus = await _hueService.GetBridgeStatusAsync();
                if (BridgeStatus?.IsConnected == true)
                {
                    AvailableLights = await _hueService.GetLightsAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading bridge status");
            }
        }
    }
}
