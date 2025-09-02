# 📋 Work Task List: Loxone NAT ↔ Hue Bridge C# Application

## 1. Project Setup

-   [x] Create solution `LoxoneHueBridge.sln` with two projects:
    -   `LoxoneHueBridge.Core` (library with CAN parser, Hue API
        wrapper, business logic)
    -   `LoxoneHueBridge.Web` (ASP.NET Core Web API + Razor
        Pages/Blazor/Tailwind-based UI)
-   [x] Add `Serilog` for structured logging (console + rolling file).
-   [x] Add configuration system (`appsettings.json` + override from
    UI).
-   [x] Setup dependency injection across both projects.

------------------------------------------------------------------------

## 2. Core: CAN Bus (Loxone NAT Protocol)

-   [x] Integrate **SocketCAN** (via `SocketCANSharp` or
    `SocketCAN.Net`) for `can0` access.
-   [x] Implement `CanListenerService`:
    -   Open `can0`, subscribe to frames.
    -   Expose `OnFrameReceived` event for consumers.
-   [x] Implement `NatParser`:
    -   Parse NAT command frames (`0x80`, `0x81`, `0x84`, `0x88`, ...).
    -   Normalize to internal events (`DigitalChanged`, `AnalogChanged`,
        `RgbwChanged`).
-   [x] Implement `NatToHueMapper`:
    -   Load `mapping.json` (NAT+DeviceId → Hue target).
    -   Route events to Hue control commands.
    -   Provide default fallback mapping if no match.

------------------------------------------------------------------------

## 3. Core: Hue Bridge Integration

-   [x] Integrate `Q42.HueApi`.
-   [x] Implement `HueService`:
    -   Discover Hue Bridge on LAN (UPnP or fallback: manual IP entry).
    -   Pairing flow: request app key (requires user pressing button).
    -   Save app key to secure storage (`appsettings.json` or SQLite).
    -   Basic controls: on/off, brightness, color, scenes.
-   [x] Implement `HueStateReporter`:
    -   Fetch list of lights/scenes.
    -   Keep cached state for Web UI display.

------------------------------------------------------------------------

## 4. Configuration & Persistence

-   [x] Implement `ConfigService` (JSON or SQLite backend).
    -   Store Hue bridge IP & app key.
    -   Store NAT↔Hue mappings.
    -   Store app settings (log level, auto-reconnect, etc.).
-   [x] Hot-reload config changes without restart.

------------------------------------------------------------------------

## 5. Web UI (ASP.NET Core)

-   [x] Base project setup: ASP.NET Core Minimal API or MVC with Razor
    Pages.
-   [ ] Add TailwindCSS for styling.
-   [x] Pages:
    -   **Dashboard**: show bridge status (Hue connected? CAN
        connected?).
    -   [x] **Pairing**: start Hue bridge pairing flow, show instructions,
        confirm success.
    -   [x] **Unpairing**: delete app key, reset Hue config.
    -   [ ] **Mappings**: list NAT devices (from observed CAN frames), allow
        mapping to Hue lights/groups.
    -   [x] **Logs**: live stream of events (CAN frames + Hue actions).
    -   [x] **Settings**: CAN bitrate, log level, restart service,
        export/import config.
-   [x] Implement WebSocket or SignalR for live log updates.

------------------------------------------------------------------------

## 6. Background Services

-   [x] `CanBackgroundService` (hosted service): reads frames, pushes
    events.
-   [x] `HueBackgroundService` (optional): periodic sync of Hue lights &
    scenes.
-   [x] `HeartbeatService`: send periodic "still alive" status to
    logs/UI.

------------------------------------------------------------------------

## 7. Testing & Simulation

-   [ ] Add **mock mode**: simulate CAN frames (instead of real `can0`).
-   [ ] Add **Hue simulator**: log commands instead of sending to bridge
    (safe dry-run mode).
-   [ ] Unit tests for `NatParser` using recorded CAN dumps.

------------------------------------------------------------------------

## 8. Packaging & Deployment

-   [ ] Add Dockerfile for deployment (optional).
-   [ ] Publish self-contained `.NET 8` ARM64 build for Raspberry Pi.
-   [ ] Add `systemd` unit file for auto-start.
-   [ ] Provide `README.md` with install/setup instructions.

------------------------------------------------------------------------

## 9. Nice-to-Have Features

-   [ ] Metrics endpoint (`/metrics`) for Prometheus/Grafana.
-   [ ] Email or MQTT notification for critical errors.
-   [ ] Configurable "scenes" that combine multiple Hue lights on NAT
    trigger.
-   [ ] Support Hue **Groups/Zones** directly in mapping.

------------------------------------------------------------------------

## 10. Stretch Goals (future)

-   [ ] Implement **Loxone NAT authorization flow** (AES
    challenge/response) for extensions that require it. (Optional: only
    if you want to emulate devices, not just listen.)
-   [ ] Add Matter support (control Hue via Matter API instead of Hue
    local API).
-   [ ] Multi-bridge support (Hue + LIFX + others).

------------------------------------------------------------------------

✅ **Deliverables for agent**:\
- C# solution with the two projects (`Core` + `Web`).\
- Config-driven services (Hue, CAN, NAT parser).\
- Web UI with pairing, unpair, logs, mappings.\
- Ready-to-run on Raspberry Pi (self-contained .NET publish + systemd
unit).
