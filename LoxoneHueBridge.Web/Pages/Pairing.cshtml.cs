using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using LoxoneHueBridge.Core.Services;
using HueApi.Models;
using HueApi;

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

                // Use the HueService which properly handles RegisterAsync and persistence
                var success = await _hueService.PairWithBridgeAsync(bridgeIp);

                if (success)
                {
                    SuccessMessage = "Successfully paired with Hue Bridge! You can now control your lights from Loxone.";
                    _logger.LogInformation("Successfully paired with Hue Bridge at {BridgeIp}", bridgeIp);
                    await LoadBridgeStatus();
                }
                else
                {
                    ErrorMessage = "Pairing failed. Make sure you pressed the button on the bridge first, then try again within 30 seconds.";
                    _logger.LogWarning("Failed to pair with Hue Bridge at {BridgeIp}", bridgeIp);
                }
            }
            catch (Exception ex)
            {
                // Check for specific Hue API errors
                if (ex.Message.Contains("link button not pressed") || 
                    ex.Message.Contains("LinkButtonNotPressed") ||
                    ex.Message.Contains("101"))
                {
                    ErrorMessage = "Pairing failed: Please press the button on the Hue Bridge first, then try again within 30 seconds.";
                    _logger.LogWarning("Link button not pressed for bridge at {BridgeIp}", bridgeIp);
                }
                else
                {
                    ErrorMessage = $"Error during pairing: {ex.Message}";
                    _logger.LogError(ex, "Error pairing with Hue Bridge");
                }
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
                IsProcessing = true;
                ProcessingMessage = "Verifying bridge at specified IP address...";

                // Validate IP address format
                if (!System.Net.IPAddress.TryParse(bridgeIp, out _))
                {
                    ErrorMessage = "Invalid IP address format.";
                    return Page();
                }

                _logger.LogInformation("Attempting manual bridge setup with IP {BridgeIp}", bridgeIp);

                // Verify it's actually a Hue Bridge by checking if we can reach the API endpoint
                try
                {
                    using var httpClient = new HttpClient();
                    httpClient.Timeout = TimeSpan.FromSeconds(10);
                    
                    // Try to access the Hue API description endpoint
                    var response = await httpClient.GetAsync($"http://{bridgeIp}/description.xml");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        
                        // Check if it contains Hue Bridge identifiers
                        if (content.Contains("Philips hue bridge", StringComparison.OrdinalIgnoreCase) || 
                            content.Contains("hue", StringComparison.OrdinalIgnoreCase))
                        {
                            // It's a Hue Bridge - now try to get bridge info for the ID
                            try
                            {
                                // Try to get bridge config (this will fail without auth, but might give us some info)
                                var configResponse = await httpClient.GetAsync($"http://{bridgeIp}/api/config");
                                if (configResponse.IsSuccessStatusCode)
                                {
                                    var configContent = await configResponse.Content.ReadAsStringAsync();
                                    // Extract bridge ID if possible from the config response
                                    var bridgeId = ExtractBridgeIdFromConfig(configContent);
                                    
                                    DiscoveredBridge = new DiscoveredBridge
                                    {
                                        IpAddress = bridgeIp,
                                        BridgeId = bridgeId ?? "manual-setup"
                                    };
                                }
                                else
                                {
                                    // Config endpoint requires auth, but bridge exists
                                    DiscoveredBridge = new DiscoveredBridge
                                    {
                                        IpAddress = bridgeIp,
                                        BridgeId = "manual-setup"
                                    };
                                }
                            }
                            catch
                            {
                                // Config call failed, but bridge exists
                                DiscoveredBridge = new DiscoveredBridge
                                {
                                    IpAddress = bridgeIp,
                                    BridgeId = "manual-setup"
                                };
                            }
                            
                            SuccessMessage = $"Hue Bridge verified at {bridgeIp}! Press the button on your bridge and click Pair.";
                            _logger.LogInformation("Successfully verified Hue Bridge at {BridgeIp}", bridgeIp);
                        }
                        else
                        {
                            ErrorMessage = $"Device at {bridgeIp} responded but doesn't appear to be a Philips Hue Bridge.";
                            _logger.LogWarning("Device at {BridgeIp} is not a Hue Bridge", bridgeIp);
                        }
                    }
                    else
                    {
                        ErrorMessage = $"No device found at {bridgeIp} or device is not accessible.";
                        _logger.LogWarning("No accessible device at {BridgeIp}", bridgeIp);
                    }
                }
                catch (TaskCanceledException)
                {
                    ErrorMessage = $"Timeout connecting to {bridgeIp}. Please check the IP address and network connectivity.";
                    _logger.LogWarning("Timeout connecting to {BridgeIp}", bridgeIp);
                }
                catch (Exception ex)
                {
                    ErrorMessage = $"Error connecting to {bridgeIp}: {ex.Message}";
                    _logger.LogError(ex, "Error connecting to {BridgeIp}", bridgeIp);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error in manual setup: {ex.Message}";
                _logger.LogError(ex, "Error in manual bridge setup");
            }
            finally
            {
                IsProcessing = false;
            }

            return Page();
        }

        private string? ExtractBridgeIdFromConfig(string configJson)
        {
            try
            {
                // Simple JSON parsing to extract bridge ID
                // Look for "bridgeid" in the config response
                var lines = configJson.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("bridgeid", StringComparison.OrdinalIgnoreCase))
                    {
                        var start = line.IndexOf('"', line.IndexOf("bridgeid", StringComparison.OrdinalIgnoreCase) + 8);
                        if (start > 0)
                        {
                            var end = line.IndexOf('"', start + 1);
                            if (end > start)
                            {
                                return line.Substring(start + 1, end - start - 1);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore parsing errors
            }
            return null;
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
