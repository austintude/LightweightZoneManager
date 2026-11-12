<#
.SYNOPSIS
  Finds every Lightweight Zone Manager build and zone config on this PC, and can
  restore an older config that still has the Ctrl+Alt zones (zones 10 to 18).

.DESCRIPTION
  Default run is READ ONLY. It reports:
    - which LightweightZoneManager.exe is running, and every copy on disk, with
      version, date and whether the build has the Ctrl+Alt+1-9 hotkeys
    - every ZoneConfig.xml (and backup) it can find, with zone count and the
      monitor setup it was saved for
    - the monitors Windows sees right now

  With -Restore -From <file>, it:
    1. exits Zone Manager
    2. copies each current ZoneConfig.xml to ZoneConfig.xml.before-restore-<time>
    3. copies the chosen file into place (AppData location, and next to the
       running exe for older builds that read from there)
    4. optionally moves zones from one monitor number to another (-MapMonitor "3=2")
    5. starts the same exe again
  Nothing is ever deleted.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\Find-ZoneSetup.ps1
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\Find-ZoneSetup.ps1 -Restore -From "C:\path\ZoneConfig.xml.backup.20261020_101500"
.EXAMPLE
  ... -Restore -From "<file>" -MapMonitor "3=2"
#>
param(
  [switch]$Restore,
  [string]$From,
  [string]$MapMonitor
)

$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.Windows.Forms

$appData   = Join-Path $env:LOCALAPPDATA 'LightweightZoneManager'
$appConfig = Join-Path $appData 'ZoneConfig.xml'

function Test-HasCtrlAlt([string]$exe) {
  # The 3.x builds carry the text "Ctrl+Alt+1" in their tray balloon message.
  try {
    $bytes = [IO.File]::ReadAllBytes($exe)
    $u16 = [Text.Encoding]::Unicode.GetString($bytes)
    return ($u16.Contains('Ctrl+Alt+1') -or $u16.Contains('zones 10-18'))
  } catch { return $null }
}

function Read-ZoneFile([string]$path) {
  try {
    [xml]$x = Get-Content -LiteralPath $path -Raw
    $zones = @($x.ZoneSettings.Zones.ZoneConfig)
    $mons  = ($zones | ForEach-Object { $_.Monitor } | Sort-Object -Unique) -join ','
    [pscustomobject]@{
      File        = $path
      Zones       = $zones.Count
      Monitors    = $mons
      SavedFor    = $x.ZoneSettings.MonitorFingerprint
      LastWritten = (Get-Item -LiteralPath $path).LastWriteTime
    }
  } catch {
    [pscustomobject]@{ File = $path; Zones = 'unreadable'; Monitors=''; SavedFor=''; LastWritten=(Get-Item -LiteralPath $path).LastWriteTime }
  }
}

Write-Host "`n=== Running Zone Manager ===" -ForegroundColor Cyan
$running = Get-Process LightweightZoneManager -ErrorAction SilentlyContinue
if ($running) {
  foreach ($p in $running) {
    $fv = (Get-Item $p.Path).VersionInfo
    "{0}`n  file version {1}, built {2}, has Ctrl+Alt hotkeys: {3}" -f $p.Path, $fv.FileVersion, (Get-Item $p.Path).LastWriteTime, (Test-HasCtrlAlt $p.Path)
  }
} else { "Not running." }

Write-Host "`n=== Startup shortcut ===" -ForegroundColor Cyan
$startup = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
$sh = New-Object -ComObject WScript.Shell
Get-ChildItem $startup -Filter *.lnk -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'Zone' } | ForEach-Object {
  "{0} -> {1}" -f $_.Name, $sh.CreateShortcut($_.FullName).TargetPath
}

Write-Host "`n=== Every copy of the exe on disk ===" -ForegroundColor Cyan
$roots = @("$env:LOCALAPPDATA\Programs", "$env:USERPROFILE\source", "$env:USERPROFILE\Documents",
           "$env:USERPROFILE\Downloads", "$env:USERPROFILE\Desktop", "$env:USERPROFILE\OneDrive") | Where-Object { Test-Path $_ }
$exes = foreach ($r in $roots) { Get-ChildItem $r -Recurse -Filter LightweightZoneManager.exe -ErrorAction SilentlyContinue }
$exes | Sort-Object LastWriteTime -Descending | ForEach-Object {
  "{0}`n  built {1}, has Ctrl+Alt hotkeys: {2}" -f $_.FullName, $_.LastWriteTime, (Test-HasCtrlAlt $_.FullName)
}

Write-Host "`n=== Every zone config and backup ===" -ForegroundColor Cyan
$cfgRoots = @($appData) + ($exes | ForEach-Object { $_.DirectoryName }) + @("$env:USERPROFILE\Downloads") | Sort-Object -Unique | Where-Object { Test-Path $_ }
$cfgs = foreach ($r in $cfgRoots) { Get-ChildItem $r -Filter 'ZoneConfig.xml*' -ErrorAction SilentlyContinue }
$cfgs | Sort-Object FullName -Unique | ForEach-Object { Read-ZoneFile $_.FullName } |
  Sort-Object LastWritten -Descending | Format-Table -AutoSize -Wrap | Out-String -Width 220

Write-Host "=== Monitors Windows sees now (Monitor numbers match the config) ===" -ForegroundColor Cyan
$i = 0
foreach ($s in [System.Windows.Forms.Screen]::AllScreens) {
  $i++
  "Monitor {0}: {1}x{2} at {3},{4}  primary={5}  {6}" -f $i, $s.Bounds.Width, $s.Bounds.Height, $s.Bounds.X, $s.Bounds.Y, $s.Primary, $s.DeviceName
}

if (-not $Restore) {
  Write-Host "`nRead only. Nothing was changed." -ForegroundColor Green
  Write-Host "Ctrl+Alt+1-9 only works with a 3.x build AND a config holding more than 9 zones."
  return
}

# ---------------- RESTORE ----------------
if (-not $From -or -not (Test-Path -LiteralPath $From)) { throw "Give -From with the config file to restore." }
$exePath = if ($running) { $running[0].Path } else { $null }
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'

Write-Host "`nExiting Zone Manager..." -ForegroundColor Yellow
$running | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

$targets = @($appConfig)
if ($exePath) { $targets += (Join-Path (Split-Path $exePath) 'ZoneConfig.xml') }
$targets = $targets | Sort-Object -Unique

[xml]$src = Get-Content -LiteralPath $From -Raw
if ($MapMonitor) {
  $pair = $MapMonitor.Split('=')
  $moved = 0
  foreach ($z in $src.ZoneSettings.Zones.ZoneConfig) { if ($z.Monitor -eq $pair[0]) { $z.Monitor = $pair[1]; $moved++ } }
  "Moved $moved zone(s) from monitor $($pair[0]) to monitor $($pair[1])."
}

New-Item -ItemType Directory -Force -Path $appData | Out-Null
foreach ($t in $targets) {
  if ((Test-Path -LiteralPath $t) -and ((Resolve-Path -LiteralPath $t).Path -ne (Resolve-Path -LiteralPath $From).Path)) {
    Copy-Item -LiteralPath $t -Destination "$t.before-restore-$stamp"
    "Backed up $t"
  }
  $src.Save($t)
  "Wrote $t ($(@($src.ZoneSettings.Zones.ZoneConfig).Count) zones)"
}

if ($exePath) { Start-Process $exePath -WorkingDirectory (Split-Path $exePath); "Restarted $exePath" }
Write-Host "Done. Test Ctrl+Shift+1 and Ctrl+Alt+1 on a window." -ForegroundColor Green
