using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using LoxoneHueBridge.Core.Services;
using LoxoneHueBridge.Core.Models;
using LoxoneHueBridge.Core.Helpers;

namespace LoxoneHueBridge.Web.Pages
{
    public class MappingsModel : PageModel
    {
        private readonly IMappingService _mappingService;
        private readonly ILoxoneLinkService _loxoneLinkService;
        private readonly ILogger<MappingsModel> _logger;

        public MappingsModel(IMappingService mappingService, ILoxoneLinkService loxoneLinkService, ILogger<MappingsModel> logger)
        {
            _mappingService = mappingService;
            _loxoneLinkService = loxoneLinkService;
            _logger = logger;
        }

        public List<HueMapping> Mappings { get; set; } = new();
        public List<ObservedLoxoneDevice> ObservedLoxoneDevices { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public string? SuccessMessage { get; set; }

        public async Task OnGetAsync()
        {
            await LoadMappings();
        }

        public async Task<IActionResult> OnPostAddAsync(int natDeviceId, string extensionSerial, string deviceSerial, string mappingType, string hueTargetType, string hueTargetId, string? description)
        {
            try
            {
                if (natDeviceId <= 0 || natDeviceId > 65535)
                {
                    ErrorMessage = "NAT Device ID must be between 1 and 65535.";
                    await LoadMappings();
                    return Page();
                }

                if (!HexFormatHelper.IsValidHexFormat(extensionSerial))
                {
                    ErrorMessage = "Invalid extension serial format. Expected format like 12:34:56:78.";
                    await LoadMappings();
                    return Page();
                }

                if (!HexFormatHelper.IsValidHexFormat(deviceSerial))
                {
                    ErrorMessage = "Invalid device serial format. Expected format like 12:34:56:79.";
                    await LoadMappings();
                    return Page();
                }

                // Check if mapping already exists
                var existingMapping = await _mappingService.GetMappingAsync(extensionSerial, deviceSerial);
                if (existingMapping != null)
                {
                    ErrorMessage = $"A mapping for Extension {extensionSerial}/Device {deviceSerial} already exists.";
                    await LoadMappings();
                    return Page();
                }

                // Parse the Hue target ID to Guid
                if (!Guid.TryParse(hueTargetId, out var hueTargetGuid))
                {
                    ErrorMessage = "Invalid Hue target ID format.";
                    await LoadMappings();
                    return Page();
                }

                var mapping = new HueMapping(
                    extensionSerial,
                    deviceSerial,
                    hueTargetGuid,
                    hueTargetType,
                    mappingType,
                    new Dictionary<string, object>
                    {
                        ["Description"] = description ?? $"Extension {extensionSerial}/Device {deviceSerial} → {hueTargetType} {hueTargetId}"
                    }
                );

                await _mappingService.AddMappingAsync(mapping);
                SuccessMessage = $"Successfully added mapping for Extension {extensionSerial}/Device {deviceSerial}.";
                
                _logger.LogInformation("Added new mapping: Extension {ExtensionSerial}/Device {DeviceSerial} → {HueTargetType} {HueTargetId}", 
                    extensionSerial, deviceSerial, hueTargetType, hueTargetId);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error adding mapping: {ex.Message}";
                _logger.LogError(ex, "Error adding mapping for NAT Device {NatDeviceId}", natDeviceId);
            }

            await LoadMappings();
            return Page();
        }

        public async Task<IActionResult> OnPostDeleteAsync(string extensionSerial, string deviceSerial)
        {
            try
            {
                await _mappingService.RemoveMappingAsync(extensionSerial, deviceSerial);
                SuccessMessage = $"Successfully deleted mapping for Extension {extensionSerial}/Device {deviceSerial}.";
                
                _logger.LogInformation("Deleted mapping for Extension {ExtensionSerial}/Device {DeviceSerial}", 
                    extensionSerial, deviceSerial);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error deleting mapping: {ex.Message}";
                _logger.LogError(ex, "Error deleting mapping for Extension {ExtensionSerial}/Device {DeviceSerial}", 
                    extensionSerial, deviceSerial);
            }

            await LoadMappings();
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
            return $"Extension {mapping.ExtensionSerial}/Device {mapping.DeviceSerial} → {mapping.HueTargetType} {mapping.HueTargetId}";
        }
    }
}
