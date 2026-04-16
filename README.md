# InputBusX - Controller Middleware for Windows

A professional-grade controller input middleware for Windows, similar to DS4Windows. Captures physical controller inputs, processes them through a configurable macro engine and filter pipeline, and outputs to a virtual Xbox 360 controller via ViGEmBus.

## Architecture

```
Physical Controller → Input Layer (XInput) → Macro Engine → Filters → Output Layer (ViGEm) → Virtual Xbox 360
```

### Project Structure

```
InputBusX/
├── src/
│   ├── InputBusX.Domain/          # Core entities, interfaces, enums, value objects
│   │   ├── Entities/              # GamepadState, Profile, MacroDefinition, FilterSettings
│   │   ├── Enums/                 # GamepadButton, MacroType, AnalogAxis, etc.
│   │   ├── Interfaces/            # IInputProvider, IOutputController, IMacroProcessor, etc.
│   │   └── ValueObjects/          # StickPosition, TriggerValue
│   │
│   ├── InputBusX.Application/     # Business logic, pipeline, services
│   │   ├── Filters/               # CompositeInputFilter (deadzone, anti-deadzone, curve, smoothing)
│   │   ├── MacroEngine/           # MacroProcessor (no-recoil, auto-fire, remap, sequence, toggle)
│   │   ├── Pipeline/              # InputPipeline orchestrating the full flow
│   │   ├── Services/              # ProfileManagerService
│   │   └── Interfaces/            # IInputPipeline
│   │
│   ├── InputBusX.Infrastructure/  # Platform-specific implementations
│   │   ├── Input/                 # XInputProvider (P/Invoke), ProcessMonitor
│   │   ├── Output/                # ViGEmOutputController (Nefarius.ViGEm.Client)
│   │   ├── Configuration/         # JsonConfigurationStore (hot-reload via FileSystemWatcher)
│   │   ├── Logging/               # InMemoryLogSink, SerilogSinkAdapter
│   │   └── Plugins/               # IPlugin, PluginLoader (assembly-based)
│   │
│   └── InputBusX.UI/             # Avalonia UI (MVVM with CommunityToolkit.Mvvm)
│       ├── Views/                 # MainWindow, DashboardView, MacroEditorView, etc.
│       ├── ViewModels/            # MainViewModel, DashboardViewModel, etc.
│       ├── Converters/            # BoolToBrush, StickToCanvas, Percentage
│       ├── Styles/                # Catppuccin Mocha dark theme
│       └── Services/              # ServiceLocator (DI container)
│
├── tests/
│   └── InputBusX.Tests/          # xUnit + FluentAssertions + Moq
│       ├── Domain/                # GamepadState, value objects tests
│       ├── Application/           # Filters, MacroProcessor tests
│       └── Infrastructure/        # Config store tests
│
└── config/
    └── settings.json              # Example configuration with 3 profiles
```

### Design Principles

- **SOLID**: Each class has a single responsibility; dependencies are injected via interfaces
- **MVVM**: ViewModels use CommunityToolkit.Mvvm source generators; Views are pure AXAML
- **Clean Architecture**: Domain has zero dependencies; Application depends only on Domain; Infrastructure implements Domain interfaces; UI wires everything together
- **Testable**: All core logic is behind interfaces with unit tests

## Prerequisites

1. **.NET 8 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
2. **ViGEmBus Driver** — [Download latest release](https://github.com/nefarius/ViGEmBus/releases)
3. **Xbox controller** connected via USB or Bluetooth

## Build & Run

```bash
# Restore dependencies
dotnet restore

# Build
dotnet build

# Run the application
dotnet run --project src/InputBusX.UI

# Run tests
dotnet test

# Publish a portable Windows x64 build
dotnet publish src/InputBusX.UI/InputBusX.UI.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true
```

The portable publish includes the `config/` folder next to the executable, so `config/settings.json` is available at runtime.

## Configuration

Configuration is stored in `config/settings.json` and supports **hot reload** — changes are picked up automatically without restarting.

### Macro Types

| Type | Description |
|------|-------------|
| `noRecoil` | Compensates for weapon recoil by adjusting right stick |
| `autoFire` | Rapid-fires a button at configurable intervals |
| `autoPing` | Pulses a button press periodically |
| `remap` | Remaps one button to another |
| `sequence` | Executes a sequence of button presses |
| `toggle` | Converts a held button to a toggle |

### Filter Pipeline

1. **Deadzone** — Ignores small stick movements
2. **Anti-Deadzone** — Adds minimum output to overcome in-game deadzones
3. **Response Curve** — Power function to adjust sensitivity (exponent: `<1` = more sensitive, `>1` = less sensitive)
4. **Smoothing** — Exponential moving average to reduce jitter

### Profiles

- Multiple profiles with different macros and filter settings
- Auto-switch profiles based on foreground process name
- Create, duplicate, delete profiles from the UI

## UI Features

- **Dashboard**: Real-time visualization of sticks, triggers, and buttons
- **Macro Editor**: Visual macro configuration with sliders and dropdowns
- **Profiles**: Profile management with process association
- **Filters**: Deadzone, anti-deadzone, response curve, smoothing controls
- **Logs**: Searchable structured log viewer

## Accessibility

- Full keyboard navigation
- Descriptive `AutomationProperties.Name` on all interactive elements
- High contrast support
- Scalable font sizes
- No exclusive reliance on color (text labels accompany all indicators)

## Plugin System

Place `.dll` files implementing `IPlugin` in a `plugins/` directory. Plugins are loaded at startup and can process gamepad state in the pipeline.

```csharp
public interface IPlugin
{
    string Name { get; }
    string Version { get; }
    string Description { get; }
    void Initialize();
    GamepadState Process(GamepadState state);
    void Shutdown();
}
```

## License

MIT
