# Zone Configuration Guide

## Overview

Lightweight Zone Manager stores zone configurations in `ZoneConfig.xml` in the application directory. You can have **unlimited zones** across **unlimited monitors**!

## Configuration File Format

The configuration file uses XML format:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ZoneSettings>
  <Zones>
    <ZoneConfig>
      <Monitor>1</Monitor>
      <X>0</X>
      <Y>0</Y>
      <Width>50</Width>
      <Height>50</Height>
      <Name>Top-Left Quarter</Name>
    </ZoneConfig>
    <!-- More zones here -->
  </Zones>
  <Version>1</Version>
  <MonitorFingerprint>2:1920x1080@0,0;1920x1080@1920,0</MonitorFingerprint>
</ZoneSettings>
```

## Zone Properties

| Property | Type | Description | Example |
|----------|------|-------------|---------|
| `Monitor` | Integer | Monitor number (1-based) | `1` for primary monitor |
| `X` | Double | Left position as % of monitor width | `0` = left edge, `50` = center |
| `Y` | Double | Top position as % of monitor height | `0` = top edge, `50` = center |
| `Width` | Double | Zone width as % of monitor width | `50` = half width, `100` = full width |
| `Height` | Double | Zone height as % of monitor height | `50` = half height, `100` = full height |
| `Name` | String | Descriptive name (optional) | `"Left Half"` |

**Important:** All positions and sizes are percentages (0-100), relative to the monitor's working area (excluding taskbar).

## Default Zone Layouts

### Primary Monitor (Monitor 1)
6 zones total:
1. **Top-Left Quarter**: 0%, 0%, 50%, 50%
2. **Top-Right Quarter**: 50%, 0%, 50%, 50%
3. **Bottom-Left Quarter**: 0%, 50%, 50%, 50%
4. **Bottom-Right Quarter**: 50%, 50%, 50%, 50%
5. **Left Half**: 0%, 0%, 50%, 100% (overlaps zones 1 & 3)
6. **Right Half**: 50%, 0%, 50%, 100% (overlaps zones 2 & 4)

### Secondary Monitors (Monitor 2+)
3 zones each:
1. **Top Half**: 0%, 0%, 100%, 50%
2. **Bottom Half**: 0%, 50%, 100%, 50%
3. **Full Screen**: 0%, 0%, 100%, 100%

## Creating Custom Zones

### Method 1: Visual Editor (Recommended)
1. Right-click tray icon → **"Edit Zones"**
2. Drag zones to move them
3. Drag corners/edges to resize
4. Right-click tray → **"Save Current Layout"**

### Method 2: Manual XML Editing

1. Right-click tray icon → **"Open Config Folder"**
2. Edit `ZoneConfig.xml` in a text editor
3. Add new `<ZoneConfig>` blocks within `<Zones>`
4. Right-click tray → **"Reload Config"**

**Example: Adding a Center Zone**

```xml
<ZoneConfig>
  <Monitor>1</Monitor>
  <X>25</X>
  <Y>25</Y>
  <Width>50</Width>
  <Height>50</Height>
  <Name>Center</Name>
</ZoneConfig>
```

## Common Layouts

### Three Column Layout (Monitor 1)
```xml
<!-- Left Third -->
<ZoneConfig>
  <Monitor>1</Monitor>
  <X>0</X>
  <Y>0</Y>
  <Width>33.33</Width>
  <Height>100</Height>
  <Name>Left Third</Name>
</ZoneConfig>

<!-- Center Third -->
<ZoneConfig>
  <Monitor>1</Monitor>
  <X>33.33</X>
  <Y>0</Y>
  <Width>33.33</Width>
  <Height>100</Height>
  <Name>Center Third</Name>
</ZoneConfig>

<!-- Right Third -->
<ZoneConfig>
  <Monitor>1</Monitor>
  <X>66.67</X>
  <Y>0</Y>
  <Width>33.33</Width>
  <Height>100</Height>
  <Name>Right Third</Name>
</ZoneConfig>
```

### Grid Layout (9 zones)
```xml
<!-- Top Row -->
<ZoneConfig><Monitor>1</Monitor><X>0</X><Y>0</Y><Width>33.33</Width><Height>33.33</Height><Name>TL</Name></ZoneConfig>
<ZoneConfig><Monitor>1</Monitor><X>33.33</X><Y>0</Y><Width>33.33</Width><Height>33.33</Height><Name>TC</Name></ZoneConfig>
<ZoneConfig><Monitor>1</Monitor><X>66.67</X><Y>0</Y><Width>33.33</Width><Height>33.33</Height><Name>TR</Name></ZoneConfig>

<!-- Middle Row -->
<ZoneConfig><Monitor>1</Monitor><X>0</X><Y>33.33</Y><Width>33.33</Width><Height>33.33</Height><Name>ML</Name></ZoneConfig>
<ZoneConfig><Monitor>1</Monitor><X>33.33</X><Y>33.33</Y><Width>33.33</Width><Height>33.33</Height><Name>MC</Name></ZoneConfig>
<ZoneConfig><Monitor>1</Monitor><X>66.67</X><Y>33.33</Y><Width>33.33</Width><Height>33.33</Height><Name>MR</Name></ZoneConfig>

<!-- Bottom Row -->
<ZoneConfig><Monitor>1</Monitor><X>0</X><Y>66.67</Y><Width>33.33</Width><Height>33.33</Height><Name>BL</Name></ZoneConfig>
<ZoneConfig><Monitor>1</Monitor><X>33.33</X><Y>66.67</Y><Width>33.33</Width><Height>33.33</Height><Name>BC</Name></ZoneConfig>
<ZoneConfig><Monitor>1</Monitor><X>66.67</X><Y>66.67</Y><Width>33.33</Width><Height>33.33</Height><Name>BR</Name></ZoneConfig>
```

### Ultra-Wide Monitor Layout
```xml
<!-- Four equal columns -->
<ZoneConfig><Monitor>1</Monitor><X>0</X><Y>0</Y><Width>25</Width><Height>100</Height><Name>Q1</Name></ZoneConfig>
<ZoneConfig><Monitor>1</Monitor><X>25</X><Y>0</Y><Width>25</Width><Height>100</Height><Name>Q2</Name></ZoneConfig>
<ZoneConfig><Monitor>1</Monitor><X>50</X><Y>0</Y><Width>25</Width><Height>100</Height><Name>Q3</Name></ZoneConfig>
<ZoneConfig><Monitor>1</Monitor><X>75</X><Y>0</Y><Width>25</Width><Height>100</Height><Name>Q4</Name></ZoneConfig>

<!-- Large center with side panels -->
<ZoneConfig><Monitor>1</Monitor><X>0</X><Y>0</Y><Width>20</Width><Height>100</Height><Name>Left Panel</Name></ZoneConfig>
<ZoneConfig><Monitor>1</Monitor><X>20</X><Y>0</Y><Width>60</Width><Height>100</Height><Name>Main</Name></ZoneConfig>
<ZoneConfig><Monitor>1</Monitor><X>80</X><Y>0</Y><Width>20</Width><Height>100</Height><Name>Right Panel</Name></ZoneConfig>
```

## Multi-Monitor Setups

### 3 Monitor Setup Example
```xml
<!-- Monitor 1 (Primary): 6 zones as default -->
<!-- Monitor 2 (Left): 2 vertical zones -->
<ZoneConfig><Monitor>2</Monitor><X>0</X><Y>0</Y><Width>100</Width><Height>50</Height><Name>M2 Top</Name></ZoneConfig>
<ZoneConfig><Monitor>2</Monitor><X>0</X><Y>50</Y><Width>100</Width><Height>50</Height><Name>M2 Bottom</Name></ZoneConfig>

<!-- Monitor 3 (Right): Full screen only -->
<ZoneConfig><Monitor>3</Monitor><X>0</X><Y>0</Y><Width>100</Width><Height>100</Height><Name>M3 Full</Name></ZoneConfig>
```

## Advanced Tips

### Overlapping Zones
You can create overlapping zones! The **last zone in the XML** will appear on top.

Example: Create a large "Main" zone that overlaps with smaller zones:
```xml
<!-- Small corner zones -->
<ZoneConfig><Monitor>1</Monitor><X>0</X><Y>0</Y><Width>20</Width><Height>20</Height><Name>TL Corner</Name></ZoneConfig>
<ZoneConfig><Monitor>1</Monitor><X>80</X><Y>0</Y><Width>20</Width><Height>20</Height><Name>TR Corner</Name></ZoneConfig>

<!-- Large main zone (drawn on top) -->
<ZoneConfig><Monitor>1</Monitor><X>10</X><Y>10</Y><Width>80</Width><Height>80</Height><Name>Main Work Area</Name></ZoneConfig>
```

### Zone Numbering
- Zones are numbered 1, 2, 3... in the order they appear in the XML
- Hotkeys (Ctrl+Shift+1-9) only work for zones 1-9
- Zones 10+ work with drag & drop
- To change zone numbers, reorder the `<ZoneConfig>` blocks in the XML

### Monitor Detection
The `MonitorFingerprint` field tracks your monitor setup:
- Format: `Count:Width1xHeight1@X1,Y1;Width2xHeight2@X2,Y2;...`
- Automatically updated when you save zones
- Used to detect when monitors change
- If monitors change, you'll get a warning to reset zones

## Troubleshooting

**Zones not showing up?**
- Check that `Monitor` number is valid (1 to number of connected monitors)
- Verify percentages are between 0-100
- Check XML syntax is valid

**Zones in wrong position?**
- Use "Reset to Defaults" to regenerate for current monitor setup
- Or use the visual editor to reposition

**Need more than 9 hotkey zones?**
- Only zones 1-9 support hotkeys (Windows limitation)
- Zones 10+ work perfectly with drag & drop
- Reorder zones in XML to control which get hotkeys

**Changed monitors?**
- Use "Reset to Defaults" to auto-generate zones for new setup
- Or manually edit `Monitor` numbers in the XML

## Backup & Restore

**Automatic Backups:**
- Corrupted configs are auto-backed up as `ZoneConfig.xml.backup.YYYYMMDD_HHMMSS`
- Found in the same folder as `ZoneConfig.xml`

**Manual Backup:**
1. Right-click tray → "Open Config Folder"
2. Copy `ZoneConfig.xml` to a safe location
3. To restore: Copy it back and "Reload Config"

## Examples for Specific Use Cases

### Software Development
```xml
<!-- Main editor: large left zone -->
<ZoneConfig><Monitor>1</Monitor><X>0</X><Y>0</Y><Width>70</Width><Height>100</Height><Name>Editor</Name></ZoneConfig>

<!-- Side panels for terminals/docs -->
<ZoneConfig><Monitor>1</Monitor><X>70</X><Y>0</Y><Width>30</Width><Height>50</Height><Name>Terminal</Name></ZoneConfig>
<ZoneConfig><Monitor>1</Monitor><X>70</X><Y>50</Y><Width>30</Width><Height>50</Height><Name>Docs</Name></ZoneConfig>
```

### Video Editing
```xml
<!-- Timeline at bottom -->
<ZoneConfig><Monitor>1</Monitor><X>0</X><Y>70</Y><Width>100</Width><Height>30</Height><Name>Timeline</Name></ZoneConfig>

<!-- Preview and effects top -->
<ZoneConfig><Monitor>1</Monitor><X>0</X><Y>0</Y><Width>70</Width><Height>70</Height><Name>Preview</Name></ZoneConfig>
<ZoneConfig><Monitor>1</Monitor><X>70</X><Y>0</Y><Width>30</Width><Height>70</Height><Name>Effects</Name></ZoneConfig>
```

### Streaming Setup
```xml
<!-- OBS: large main monitor -->
<ZoneConfig><Monitor>1</Monitor><X>0</X><Y>0</Y><Width>100</Width><Height>100</Height><Name>OBS</Name></ZoneConfig>

<!-- Chat and monitoring on monitor 2 -->
<ZoneConfig><Monitor>2</Monitor><X>0</X><Y>0</Y><Width>50</Width><Height>100</Height><Name>Chat</Name></ZoneConfig>
<ZoneConfig><Monitor>2</Monitor><X>50</X><Y>0</Y><Width>50</Width><Height>100</Height><Name>Dashboard</Name></ZoneConfig>
```

## Need Help?

- Check "Usage Instructions" in the tray menu
- Look at `UPGRADE_INSTRUCTIONS.md` for version info
- Report issues at: https://github.com/anthropics/claude-code/issues
