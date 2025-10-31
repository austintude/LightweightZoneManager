# Upgrading to v2.0-Refactored

## The Problem
The application runs in the system tray and stays running even when you close windows. This means you need to fully exit the old version before testing the new one.

## How to Upgrade

### Step 1: Stop the Old Version
1. Find the **Zone Manager** icon in your system tray (bottom-right of taskbar)
2. **Right-click** the icon
3. Click **"Exit"**
4. Wait a moment for the process to fully close

### Step 2: Rebuild the New Version
Open a command prompt and run:
```bash
cd "c:\Users\danie\source\repos\LightweightZoneManager"
powershell -Command "& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' LightweightZoneManager.csproj /p:Configuration=Release /t:Rebuild"
```

### Step 3: Run the New Version
```bash
cd bin\Release
.\LightweightZoneManager.exe
```

## How to Tell Which Version You're Running

### Method 1: Hover over tray icon
The tooltip will show:
- **Old version**: "Lightweight Zone Manager - Ctrl+Shift+` to show zones"
- **New version**: "Zone Manager v2.0-Refactored - Ctrl+Shift+` to show zones"

### Method 2: Check Usage Instructions
1. Right-click tray icon
2. Click "Usage Instructions"
3. Look at the top of the dialog:
   - **Old version**: No version info
   - **New version**: Shows "LIGHTWEIGHT ZONE MANAGER v2.0-Refactored" with build date

## Testing Edit Zones Functionality

Once you're running v2.0:

1. Right-click tray icon → **"Show Debug Console"** (this shows helpful logs)
2. Right-click tray icon → **"Edit Zones"**
3. You should see:
   - Console output showing "=== EDIT ZONES CLICKED (v2.0-Refactored) ==="
   - Zones appear with white resize handles in corners
   - Balloon tip with instructions
4. Try dragging a zone to move it
5. Try dragging a corner to resize it
6. When done: Right-click tray → **"Save Current Layout"**

## If Edit Zones Still Doesn't Work

Check the debug console for error messages. If you see "No zones available to edit", this means:
- Your monitor configuration changed
- Zones were not loaded properly
- Solution: Right-click tray → **"Reset to Defaults"**

## What's New in v2.0

- **Monitor change detection**: Warns you when monitor setup changes
- **Better error messages**: Clear warnings about missing monitors
- **Modular code**: Split into 9 focused files for maintainability
- **Version info**: Always know which version you're running
- **Debug logging**: Better troubleshooting capabilities
- **Improved config**: Now stores monitor fingerprint for validation

## Troubleshooting

**Q: Build fails with "file is locked"**
A: The app is still running. Exit it from the system tray first.

**Q: How do I reset my zones?**
A: Right-click tray icon → "Reset to Defaults"

**Q: Zones disappeared after monitor change**
A: Right-click tray icon → "Reset to Defaults" to create fresh zones for your current setup.

**Q: How do I know if my config is corrupted?**
A: Check `bin\Release\ZoneConfig.xml` - if it references Monitor 2 but you only have 1 monitor, reset to defaults.
