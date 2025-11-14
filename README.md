# Lightweight Zone Manager

A lightweight, customizable window snapping tool for Windows that lets you organize your workspace with custom zones across multiple monitors.

![Version](https://img.shields.io/badge/version-3.0--Unlimited-blue)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)
![.NET](https://img.shields.io/badge/.NET-Framework%204.8-purple)

## Features

- **Drag & Drop Snapping**: Hold Ctrl while dragging any window to snap it to zones
- **Unlimited Zones**: Create as many zones as you need (not limited to 9!)
- **Multi-Monitor Support**: Works seamlessly across unlimited monitors
- **Visual Zone Editor**: Drag to move zones, resize with corner handles
- **Hotkey Support**: Ctrl+Shift+1-9 for quick snapping (requires admin mode)
- **Monitor Change Detection**: Automatically detects when your monitor setup changes
- **Percentage-Based Layout**: Zones adapt to any screen resolution
- **Lightweight**: Runs quietly in the system tray, minimal resource usage

## Quick Start

### Installation

1. Download the latest release or build from source
2. Run `LightweightZoneManager.exe`
3. The app will appear in your system tray

### Basic Usage

**Drag & Drop (Recommended - No Admin Required):**
1. Hold **Ctrl** key
2. Start dragging any window
3. Zones will appear automatically
4. Drag over a zone to highlight it
5. Release mouse to snap the window

**Hotkeys (Admin Mode Only):**
- `Ctrl+Shift+\`` - Show/hide zones
- `Ctrl+Shift+1-9` - Snap active window to zones 1-9

## Configuration

### Default Layout

**Primary Monitor (Monitor 1):** 6 zones
- Top-Left Quarter
- Top-Right Quarter
- Bottom-Left Quarter
- Bottom-Right Quarter
- Left Half
- Right Half

**Secondary Monitors:** 3 zones each
- Top Half
- Bottom Half
- Full Screen

### Customizing Zones

**Method 1: Visual Editor (Easiest)**
1. Right-click tray icon → **Edit Zones**
2. Drag zones to move them
3. Drag corners/edges to resize
4. Right-click tray → **Save Current Layout**

**Method 2: Manual XML Editing**
1. Right-click tray icon → **Open Config Folder**
2. Edit `ZoneConfig.xml`
3. Right-click tray → **Reload Config**

See [ZONE_CONFIGURATION_GUIDE.md](ZONE_CONFIGURATION_GUIDE.md) for detailed examples and layouts.

## System Requirements

- Windows 7 or later
- .NET Framework 4.8
- Admin rights (optional, only needed for hotkeys)

## Building from Source

### Prerequisites
- Visual Studio 2022 or later (Community Edition works)
- .NET Framework 4.8 SDK

### Build Steps

```bash
# Clone the repository
git clone https://github.com/YOUR_USERNAME/LightweightZoneManager.git
cd LightweightZoneManager

# Build using MSBuild (or open the .sln in Visual Studio)
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" LightweightZoneManager.csproj /p:Configuration=Release /t:Rebuild

# Run the executable
cd bin\Release
.\LightweightZoneManager.exe
```

## Project Structure

```
LightweightZoneManager/
├── ZoneManager.cs              # Main application logic
├── NativeApi.cs                # Windows API declarations
├── MonitorManager.cs           # Monitor detection & validation
├── ZoneConfiguration.cs        # Config models & persistence
├── ZoneOverlay.cs              # Read-only zone display
├── EditableZoneOverlay.cs      # Interactive zone editing
├── DragZoneOverlay.cs          # Drag & drop highlighting
├── GlobalMouseHook.cs          # Mouse event hook
├── GlobalKeyboardHook.cs       # Keyboard hotkey hook
├── ZONE_CONFIGURATION_GUIDE.md # Detailed configuration guide
└── UPGRADE_INSTRUCTIONS.md     # Version upgrade help
```

## Troubleshooting

**Zones not appearing during drag?**
- Make sure you're holding Ctrl BEFORE starting to drag
- Try running as administrator (right-click → Run as administrator)

**Zones in wrong position after monitor change?**
- Right-click tray → "Reset to Defaults" to regenerate zones

**Edit mode not working?**
- Ensure you're running the latest version (hover over tray icon)
- Check system tray for the Zone Manager icon and exit any old instances

**Need more than 9 hotkey zones?**
- Only zones 1-9 support hotkeys (Windows limitation)
- Zones 10+ work perfectly with drag & drop

See [UPGRADE_INSTRUCTIONS.md](UPGRADE_INSTRUCTIONS.md) for more help.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

### Development Guidelines
- Keep code modular and well-commented
- Test on multiple monitor configurations
- Update documentation for user-facing changes
- Follow existing code style

## Version History

### v3.0-Unlimited (Current)
- Removed 9-zone limitation
- Added unlimited multi-monitor support
- Comprehensive debug logging

### v2.3-ZOrderFixed
- Fixed zone selection order to match visual z-order

### v2.2-EditModeFixed
- Fixed edit mode zones disappearing on mouse release

### v2.0-Refactored
- Major refactor: Split monolithic file into 9 focused modules
- Added monitor change detection
- Improved error messages and debugging

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Acknowledgments

- Inspired by Windows PowerToys FancyZones
- Built with Windows Forms and .NET Framework
- Created to be lightweight and fast

## Support

- **Issues**: Report bugs via [GitHub Issues](https://github.com/YOUR_USERNAME/LightweightZoneManager/issues)
- **Documentation**: See [ZONE_CONFIGURATION_GUIDE.md](ZONE_CONFIGURATION_GUIDE.md)
- **Discussions**: Share your custom layouts and ideas!

---

**Made with** ❤️ **for productivity enthusiasts**
