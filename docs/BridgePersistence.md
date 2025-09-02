# Bridge Persistence Implementation

## How Bridge Persistence Works

### Initial State (Fresh Install)
```json
{
  "LoxoneHueBridge": {
    "HueBridge": {
      "AutoDiscover": true,
      "ManualIpAddress": null,
      "AppKey": null
    }
  }
}
```

### After Discovery + Pairing
When a bridge is discovered and successfully paired, the configuration is automatically updated:

```json
{
  "LoxoneHueBridge": {
    "HueBridge": {
      "AutoDiscover": false,
      "ManualIpAddress": "192.168.1.100", 
      "AppKey": "abcd1234-5678-90ef-ghij-klmnopqrstuv"
    }
  }
}
```

### On Application Restart
1. **Configuration loaded** from appsettings.json
2. **Bridge status restored** with both IP address and pairing credentials
3. **No rediscovery needed** - direct connection to known bridge
4. **Connection test** validates bridge is still reachable

### Benefits
- ✅ **Persistent pairing** - survives app restarts
- ✅ **Fast startup** - no rediscovery delay
- ✅ **Offline resilience** - works even if bridge temporarily unavailable
- ✅ **Easy debugging** - configuration visible in appsettings.json
- ✅ **Manual override** - can manually edit IP if needed

### Lifecycle Events

#### Discovery → Pairing
1. User clicks "Discover Bridge" 
2. Bridge found at IP `192.168.1.100`
3. User presses bridge button + clicks "Pair"
4. **Configuration automatically updated** with IP + AppKey
5. AutoDiscover set to `false` (we now have a known bridge)

#### Unpairing
1. User clicks "Unpair Bridge"
2. **Configuration cleared**: IP → null, AppKey → null
3. AutoDiscover reset to `true` (back to discovery mode)
4. Bridge must be rediscovered for future use

#### App Restart
1. Configuration loaded from file
2. If IP + AppKey exist → **Bridge immediately available**
3. If only AppKey exists → **Paired but needs rediscovery**
4. If neither exist → **Fresh discovery required**

## Technical Implementation

### Services Added
- `IConfigurationUpdateService` - Updates appsettings.json at runtime
- `ConfigurationUpdateService` - JSON file persistence implementation

### HueService Enhanced
- **Constructor**: Restores bridge status from configuration
- **PairWithBridgeAsync**: Persists bridge IP + AppKey when pairing succeeds  
- **UnpairFromBridgeAsync**: Clears all bridge configuration
- **UpdateBridgeStatusFromConfig**: Better handling of discovered vs manual bridges

### Dependency Injection
```csharp
// Added to ServiceCollectionExtensions
services.AddSingleton<IConfigurationUpdateService, ConfigurationUpdateService>();
```
