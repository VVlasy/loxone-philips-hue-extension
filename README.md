# LoxoneHueBridge

A C# .NET application that bridges Loxone NAT (CAN Bus) protocol to Philips Hue Bridge, enabling control of Hue lights from Loxone devices.

## Features

✅ **Implemented:**
- ✅ CAN Bus listener with SocketCAN support (mock mode for development/Windows)
- ✅ Loxone NAT protocol parser (supports digital, analog, and RGBW commands)
- ✅ Philips Hue Bridge integration with discovery and pairing
- ✅ Configurable device mappings (NAT device ID → Hue light/group/scene)
- ✅ Web-based dashboard with real-time status updates
- ✅ RESTful API for status and control
- ✅ Structured logging with Serilog (console + file)
- ✅ Background services for CAN monitoring and Hue management
- ✅ Configuration via appsettings.json with hot-reload support

🚧 **In Progress:**
- Pairing/Unpairing UI pages
- Mappings management interface
- Live logs viewer
- Settings configuration page
- TailwindCSS styling

📋 **Planned:**
- Mock mode for testing without real hardware
- Unit tests with recorded CAN dumps
- Docker deployment
- Raspberry Pi systemd service
- Metrics endpoint for monitoring

## Architecture

```
┌─────────────────┐    ┌────────────────────┐    ┌─────────────────┐
│   Loxone NAT    │    │  LoxoneHueBridge   │    │  Philips Hue    │
│   (CAN Bus)     │───▶│                    │───▶│    Bridge       │
│                 │    │  Core Services     │    │                 │
└─────────────────┘    │  - CanListener     │    └─────────────────┘
                       │  - NatParser       │
                       │  - MappingService  │
                       │  - HueService      │
                       └────────────────────┘
                                 ▲
                                 │
                       ┌────────────────────┐
                       │    Web Interface   │
                       │  - Dashboard       │
                       │  - Pairing         │
                       │  - Mappings        │
                       │  - Logs            │
                       └────────────────────┘
```

## Getting Started

### Prerequisites

- .NET 8 SDK
- For production: Linux system with SocketCAN support
- Philips Hue Bridge on the same network

### Development Setup

1. **Clone the repository:**
   ```bash
   git clone https://github.com/your-repo/loxone-philips-hue-extension.git
   cd loxone-philips-hue-extension
   ```

2. **Build the solution:**
   ```bash
   dotnet build LoxoneHueBridge.sln
   ```

3. **Run in development mode:**
   ```bash
   cd LoxoneHueBridge.Web
   dotnet run
   ```

4. **Open the dashboard:**
   Navigate to `http://localhost:5000` (or the URL shown in the console)

### Configuration

The application uses `appsettings.json` for configuration:

```json
{
  "LoxoneHueBridge": {
    "CanInterface": "can0",
    "CanBitrate": 125000,
    "MockMode": true,
    "HueBridge": {
      "AutoDiscover": true,
      "ManualIpAddress": null,
      "AppKey": null,
      "ApplicationName": "LoxoneHueBridge",
      "DeviceName": "LoxoneHueBridge"
    },
    "Mappings": {
      "ConfigFile": "mappings.json",
      "AutoSave": true
    }
  }
}
```

### Device Mappings

Map NAT devices to Hue targets using the `mappings.json` file:

```json
[
  {
    "NatDeviceId": 1,
    "HueTargetId": "light-id-1",
    "HueTargetType": "light",
    "MappingType": "digital",
    "Options": {
      "Description": "Living room main light"
    }
  }
]
```

**Mapping Types:**
- `digital`: On/Off control
- `analog`: Brightness control (0-100%)
- `rgbw`: Color control (Red, Green, Blue, White)

**Target Types:**
- `light`: Individual Hue light
- `group`: Hue room/zone
- `scene`: Hue scene

## API Endpoints

### Status
- `GET /api/status` - Overall system status
- `GET /api/status/can` - CAN Bus status
- `GET /api/status/hue` - Hue Bridge status

### Hue Control
- `POST /api/hue/discover` - Discover Hue Bridge
- `POST /api/hue/pair` - Pair with Hue Bridge
- `GET /api/hue/lights` - Get all lights
- `GET /api/hue/groups` - Get all groups
- `GET /api/hue/scenes` - Get all scenes

## Production Deployment

### Raspberry Pi Setup

1. **Install .NET 8 Runtime:**
   ```bash
   curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0 --runtime aspnetcore
   ```

2. **Setup CAN Interface:**
   ```bash
   sudo modprobe can
   sudo modprobe can_raw
   sudo ip link set can0 type can bitrate 125000
   sudo ip link set up can0
   ```

3. **Deploy Application:**
   ```bash
   # Publish self-contained for ARM64
   dotnet publish LoxoneHueBridge.Web -c Release -r linux-arm64 --self-contained
   
   # Copy to target directory
   sudo cp -r bin/Release/net8.0/linux-arm64/publish/* /opt/loxone-hue-bridge/
   ```

4. **Create systemd service:**
   ```ini
   [Unit]
   Description=LoxoneHueBridge
   After=network.target
   
   [Service]
   Type=notify
   ExecStart=/opt/loxone-hue-bridge/LoxoneHueBridge.Web
   Restart=always
   RestartSec=30
   User=pi
   Environment=ASPNETCORE_ENVIRONMENT=Production
   WorkingDirectory=/opt/loxone-hue-bridge
   
   [Install]
   WantedBy=multi-user.target
   ```

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [HueApi](https://github.com/michielpost/HueApi) - Modern Philips Hue API library
- [SocketCANSharp](https://github.com/derek-will/SocketCANSharp) - .NET SocketCAN implementation
- [Serilog](https://serilog.net/) - Structured logging library
- Loxone documentation for NAT protocol specifications