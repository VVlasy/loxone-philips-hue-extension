using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using LoxoneHueBridge.Core.Services;
using LoxoneHueBridge.Core.Models;

namespace LoxoneHueBridge.Web.Pages
{
    public class MappingsModel : PageModel
    {
        private readonly IMappingService _mappingService;
        private readonly ICanListenerService _canService;
        private readonly ILogger<MappingsModel> _logger;

        public MappingsModel(IMappingService mappingService, ICanListenerService canService, ILogger<MappingsModel> logger)
        {
            _mappingService = mappingService;
            _canService = canService;
            _logger = logger;
        }

        public List<HueMapping> Mappings { get; set; } = new();
        public List<ObservedNatDevice> ObservedNatDevices { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }

        public async Task OnGetAsync()
        {
            await LoadMappings();
            await LoadObservedDevices();
        }

        public async Task<IActionResult> OnPostAddAsync(int natDeviceId, string mappingType, string hueTargetType, string hueTargetId, string? description)
        {
            try
            {
                if (natDeviceId <= 0 || natDeviceId > 65535)
                {
                    ErrorMessage = "NAT Device ID must be between 1 and 65535.";
                    await LoadMappings();
                    await LoadObservedDevices();
                    return Page();
                }

                // Check if mapping already exists
                var existingMapping = await _mappingService.GetMappingAsync((uint)natDeviceId);
                if (existingMapping != null)
                {
                    ErrorMessage = $"A mapping for NAT Device ID {natDeviceId} already exists.";
                    await LoadMappings();
                    await LoadObservedDevices();
                    return Page();
                }

                // Parse the Hue target ID to Guid
                if (!Guid.TryParse(hueTargetId, out var hueTargetGuid))
                {
                    ErrorMessage = "Invalid Hue target ID format.";
                    await LoadMappings();
                    await LoadObservedDevices();
                    return Page();
                }

                var mapping = new HueMapping(
                    (uint)natDeviceId,
                    hueTargetGuid,
                    hueTargetType,
                    mappingType,
                    new Dictionary<string, object>
                    {
                        ["Description"] = description ?? $"NAT Device {natDeviceId} → {hueTargetType} {hueTargetId}"
                    }
                );

                await _mappingService.AddMappingAsync(mapping);
                SuccessMessage = $"Successfully added mapping for NAT Device {natDeviceId}.";
                
                _logger.LogInformation("Added new mapping: NAT {NatDeviceId} → {HueTargetType} {HueTargetId}", 
                    natDeviceId, hueTargetType, hueTargetId);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error adding mapping: {ex.Message}";
                _logger.LogError(ex, "Error adding mapping for NAT Device {NatDeviceId}", natDeviceId);
            }

            await LoadMappings();
            await LoadObservedDevices();
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int natDeviceId)
        {
            try
            {
                await _mappingService.RemoveMappingAsync((uint)natDeviceId);
                SuccessMessage = $"Successfully deleted mapping for NAT Device {natDeviceId}.";
                
                _logger.LogInformation("Deleted mapping for NAT Device {NatDeviceId}", natDeviceId);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error deleting mapping: {ex.Message}";
                _logger.LogError(ex, "Error deleting mapping for NAT Device {NatDeviceId}", natDeviceId);
            }

            await LoadMappings();
            await LoadObservedDevices();
            return Page();
        }

        private async Task LoadMappings()
        {
            try
            {
                Mappings = await _mappingService.GetAllMappingsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading mappings");
                ErrorMessage = "Error loading mappings.";
            }
        }

        private async Task LoadObservedDevices()
        {
            try
            {
                // Get recently observed NAT devices from CAN service
                ObservedNatDevices = await _canService.GetRecentlyObservedDevicesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading observed devices");
            }
        }
    }

    // Extension class for HueMapping to add display properties
    public static class HueMappingExtensions
    {
        public static bool IsActive(this HueMapping mapping)
        {
            // A mapping is considered active if it was used recently
            // This could be enhanced with actual usage tracking
            return true;
        }

        public static string Description(this HueMapping mapping)
        {
            if (mapping.Options?.ContainsKey("Description") == true)
            {
                return mapping.Options["Description"]?.ToString() ?? "";
            }
            return $"NAT {mapping.NatDeviceId} → {mapping.HueTargetType} {mapping.HueTargetId}";
        }
    }
}
