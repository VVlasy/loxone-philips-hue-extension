using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace LoxoneHueBridge.Core.Services;

/// <summary>
/// A service that writes configuration changes back to the appropriate appsettings file
/// while preserving the existing structure and comments.
/// </summary>
public class ConfigurationWriter
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly string _baseConfigFilePath;
    private readonly string _environmentConfigFilePath;

    public ConfigurationWriter(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
        _baseConfigFilePath = FindConfigFilePath("appsettings.json");
        _environmentConfigFilePath = FindConfigFilePath($"appsettings.{_environment.EnvironmentName}.json");
    }

    /// <summary>
    /// Updates a configuration value and saves it to the appropriate configuration file.
    /// In Development environment, updates are made to appsettings.Development.json to override production settings.
    /// </summary>
    /// <param name="key">The configuration key (e.g., "LoxoneHueBridge:HueBridge:AppKey")</param>
    /// <param name="value">The value to set</param>
    public async Task UpdateConfigurationAsync(string key, object? value)
    {
        // Determine which file to update based on environment and existing configuration
        var targetFile = DetermineTargetConfigFile(key);
        var configData = await LoadConfigurationAsync(targetFile);
        SetNestedValue(configData, key, value);
        await SaveConfigurationAsync(configData, targetFile);
    }

    /// <summary>
    /// Updates multiple configuration values in a single operation.
    /// </summary>
    /// <param name="updates">Dictionary of key-value pairs to update</param>
    public async Task UpdateConfigurationAsync(Dictionary<string, object?> updates)
    {
        // Group updates by target file
        var fileUpdates = new Dictionary<string, Dictionary<string, object?>>();
        
        foreach (var update in updates)
        {
            var targetFile = DetermineTargetConfigFile(update.Key);
            if (!fileUpdates.ContainsKey(targetFile))
            {
                fileUpdates[targetFile] = new Dictionary<string, object?>();
            }
            fileUpdates[targetFile][update.Key] = update.Value;
        }
        
        // Apply updates to each file
        foreach (var fileUpdate in fileUpdates)
        {
            var configData = await LoadConfigurationAsync(fileUpdate.Key);
            
            foreach (var update in fileUpdate.Value)
            {
                SetNestedValue(configData, update.Key, update.Value);
            }
            
            await SaveConfigurationAsync(configData, fileUpdate.Key);
        }
    }

    private string DetermineTargetConfigFile(string key)
    {
        // In development environment, always use the environment-specific file
        // to override any production settings
        if (_environment.IsDevelopment() && File.Exists(_environmentConfigFilePath))
        {
            return _environmentConfigFilePath;
        }
        
        // For production or when environment file doesn't exist, use base config
        return _baseConfigFilePath;
    }

    private string FindConfigFilePath()
    {
        // Check for configuration file in order of priority
        var possiblePaths = new[]
        {
            "appsettings.json",
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        // Default to current directory
        return Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
    }

    private string FindConfigFilePath(string fileName)
    {
        // Check for configuration file in order of priority
        var possiblePaths = new[]
        {
            fileName,
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(Directory.GetCurrentDirectory(), fileName)
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        // Default to current directory
        return Path.Combine(Directory.GetCurrentDirectory(), fileName);
    }

    private async Task<Dictionary<string, object>> LoadConfigurationAsync()
    {
        return await LoadConfigurationAsync(_baseConfigFilePath);
    }

    private async Task<Dictionary<string, object>> LoadConfigurationAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new Dictionary<string, object>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            using var document = JsonDocument.Parse(json);
            return ConvertJsonElementToObject(document.RootElement) as Dictionary<string, object>
                   ?? new Dictionary<string, object>();
        }
        catch (JsonException)
        {
            // If JSON is invalid, start with empty configuration
            return new Dictionary<string, object>();
        }
    }

    private async Task SaveConfigurationAsync(Dictionary<string, object> configData)
    {
        await SaveConfigurationAsync(configData, _baseConfigFilePath);
    }

    private async Task SaveConfigurationAsync(Dictionary<string, object> configData, string filePath)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        var json = JsonSerializer.Serialize(configData, options);
        
        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(filePath, json);
    }

    private void SetNestedValue(Dictionary<string, object> config, string key, object? value)
    {
        var parts = key.Split(':');
        var current = config;

        // Navigate to the parent of the target key
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!current.ContainsKey(parts[i]))
            {
                current[parts[i]] = new Dictionary<string, object>();
            }

            if (current[parts[i]] is not Dictionary<string, object> nestedDict)
            {
                // If the existing value is not a dictionary, replace it
                nestedDict = new Dictionary<string, object>();
                current[parts[i]] = nestedDict;
            }

            current = nestedDict;
        }

        // Set the final value
        var finalKey = parts[^1];
        if (value == null)
        {
            current.Remove(finalKey);
        }
        else
        {
            current[finalKey] = value;
        }
    }

    private object ConvertJsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                prop => prop.Name,
                prop => ConvertJsonElementToObject(prop.Value)
            ),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElementToObject).ToArray(),
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt32(out var intValue) ? intValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.ToString()
        };
    }
}
