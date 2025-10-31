using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;

namespace LightweightZoneManager
{
    public partial class ZoneManager : Form
    {
        // Version identifier for this refactored build
        private const string VERSION = "3.0-Unlimited";
        private const string BUILD_DATE = "2025-01-31";

        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private bool zonesVisible = false;
        private readonly List<Rectangle> zones = new List<Rectangle>();
        private readonly List<Form> zoneOverlays = new List<Form>();
        private readonly HashSet<IntPtr> overlayHandles = new HashSet<IntPtr>();

        private GlobalKeyboardHook keyboardHook;
        private GlobalMouseHook mouseHook;
        private ZoneConfigurationManager configManager;
        private ZoneSettings currentSettings;
        private string configPath;
        private bool editMode = false;
        private List<ZoneConfig> zoneConfigs = new List<ZoneConfig>();
        private bool isDragSnapping = false;
        private IntPtr draggedWindow = IntPtr.Zero;
        private bool ctrlWasPressed = false;
        private DateTime lastDragEnd = DateTime.MinValue;

        public ZoneManager()
        {
            InitializeComponent();

            // Set up config file path in the same directory as the executable
            configPath = Path.Combine(Application.StartupPath, "ZoneConfig.xml");
            configManager = new ZoneConfigurationManager(configPath);

            SetupTrayIcon();
            LoadZoneConfigWithMonitorCheck();
            SetupGlobalHotkey();
            SetupMouseHook();
        }

        private void InitializeComponent()
        {
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            this.Visible = false;
        }

        private void SetupTrayIcon()
        {
            Icon customIcon = CreateCustomIcon();

            trayIcon = new NotifyIcon()
            {
                Icon = customIcon,
                Visible = true,
                Text = $"Zone Manager v{VERSION} - Ctrl+Shift+` to show zones"
            };

            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Show Zones", null, ShowZones_Click);
            trayMenu.Items.Add("Edit Zones", null, EditZones_Click);
            trayMenu.Items.Add("Hide Zones", null, HideZones_Click);
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Test Snap Active Window", null, TestSnapActive_Click);
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Save Current Layout", null, SaveZones_Click);
            trayMenu.Items.Add("Reload Config", null, ReloadConfig_Click);
            trayMenu.Items.Add("Reset to Defaults", null, ResetZones_Click);
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Monitor Info", null, ShowMonitorInfo_Click);
            trayMenu.Items.Add("Restart as Admin (for hotkeys)", null, RestartAsAdmin_Click);
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Open Config Folder", null, OpenConfigFolder_Click);
            trayMenu.Items.Add("Show Debug Console", null, ShowConsole_Click);
            trayMenu.Items.Add("Usage Instructions", null, PinInstructions_Click);
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Exit", null, Exit_Click);

            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += TrayIcon_DoubleClick;
        }

        private Icon CreateCustomIcon()
        {
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                using (Pen pen = new Pen(Color.White, 1))
                {
                    g.DrawLine(pen, 5, 2, 5, 13);
                    g.DrawLine(pen, 10, 2, 10, 13);
                    g.DrawLine(pen, 2, 5, 13, 5);
                    g.DrawLine(pen, 2, 10, 13, 10);
                    g.DrawRectangle(pen, 2, 2, 11, 11);
                }
                using (Brush brush = new SolidBrush(Color.LightBlue))
                {
                    g.FillRectangle(brush, 3, 3, 2, 2);
                }
            }
            IntPtr hIcon = bitmap.GetHicon();
            return Icon.FromHandle(hIcon);
        }

        // ===============================
        // MONITOR CHANGE DETECTION & CONFIG LOADING
        // ===============================

        private void LoadZoneConfigWithMonitorCheck()
        {
            try
            {
                currentSettings = configManager.LoadConfig();

                if (currentSettings == null)
                {
                    // No config file exists - create default
                    Console.WriteLine("No config file found, creating defaults");
                    CreateDefaultZones();
                    SaveZoneConfig();
                    return;
                }

                zoneConfigs = currentSettings.Zones;

                // Check for monitor configuration changes
                string currentFingerprint = MonitorManager.GetMonitorFingerprint();
                bool monitorChanged = MonitorManager.HasMonitorConfigChanged(
                    currentSettings.MonitorFingerprint,
                    currentFingerprint
                );

                if (monitorChanged)
                {
                    HandleMonitorChange(currentSettings.MonitorFingerprint, currentFingerprint);
                }

                // Check if any zones reference missing monitors
                if (MonitorManager.HasMissingMonitors(zoneConfigs))
                {
                    int missingCount = MonitorManager.CountZonesOnMissingMonitors(zoneConfigs);
                    int currentMonitorCount = MonitorManager.GetMonitorCount();

                    var result = MessageBox.Show(
                        $"Monitor configuration mismatch detected!\n\n" +
                        $"Current monitors: {currentMonitorCount}\n" +
                        $"Zones referencing missing monitors: {missingCount}\n\n" +
                        $"These zones will be hidden until monitors are available.\n\n" +
                        $"Would you like to reset zones to match your current monitor setup?",
                        "Monitor Configuration Changed",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (result == DialogResult.Yes)
                    {
                        CreateDefaultZones();
                        SaveZoneConfig();
                        return;
                    }
                }

                if (zoneConfigs.Count == 0)
                {
                    Console.WriteLine("No zones in config, creating defaults");
                    CreateDefaultZones();
                }
                else
                {
                    BuildZonesFromConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading zone config: {ex.Message}");
                MessageBox.Show(
                    $"Error loading zone config: {ex.Message}\n\n" +
                    $"Using default zones. Your previous zones may be in a backup file.",
                    "Config Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                CreateDefaultZones();
            }
        }

        private void HandleMonitorChange(string oldFingerprint, string newFingerprint)
        {
            string changeDescription = MonitorManager.DescribeMonitorChange(oldFingerprint, newFingerprint);

            Console.WriteLine("=== MONITOR CONFIGURATION CHANGED ===");
            Console.WriteLine(changeDescription);

            // Show notification
            trayIcon.ShowBalloonTip(
                8000,
                "Monitor Configuration Changed",
                changeDescription + "\nCheck your zone configuration.",
                ToolTipIcon.Warning);
        }

        private void SaveZoneConfig()
        {
            try
            {
                // Update monitor fingerprint
                currentSettings = new ZoneSettings
                {
                    Zones = zoneConfigs,
                    Version = 1,
                    MonitorFingerprint = MonitorManager.GetMonitorFingerprint()
                };

                configManager.SaveConfig(currentSettings);

                Console.WriteLine($"Saved {zoneConfigs.Count} zones with monitor fingerprint: {currentSettings.MonitorFingerprint}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving zone config: {ex.Message}");
                MessageBox.Show($"Error saving zone config: {ex.Message}", "Save Error");
            }
        }

        private void CreateDefaultZones()
        {
            zoneConfigs.Clear();
            int monitorCount = MonitorManager.GetMonitorCount();

            Console.WriteLine($"Creating default zones for {monitorCount} monitor(s)");

            // Create zones for each monitor
            for (int monitor = 1; monitor <= monitorCount; monitor++)
            {
                if (monitor == 1)
                {
                    // Primary monitor: 6 zones (4 quarters + 2 halves)
                    zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 0, Y = 0, Width = 50, Height = 50, Name = "M1 Top-Left" });
                    zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 50, Y = 0, Width = 50, Height = 50, Name = "M1 Top-Right" });
                    zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 0, Y = 50, Width = 50, Height = 50, Name = "M1 Bottom-Left" });
                    zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 50, Y = 50, Width = 50, Height = 50, Name = "M1 Bottom-Right" });
                    zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 0, Y = 0, Width = 50, Height = 100, Name = "M1 Left Half" });
                    zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 50, Y = 0, Width = 50, Height = 100, Name = "M1 Right Half" });
                }
                else
                {
                    // Secondary monitors: 3 zones (top half, bottom half, full)
                    zoneConfigs.Add(new ZoneConfig { Monitor = monitor, X = 0, Y = 0, Width = 100, Height = 50, Name = $"M{monitor} Top" });
                    zoneConfigs.Add(new ZoneConfig { Monitor = monitor, X = 0, Y = 50, Width = 100, Height = 50, Name = $"M{monitor} Bottom" });
                    zoneConfigs.Add(new ZoneConfig { Monitor = monitor, X = 0, Y = 0, Width = 100, Height = 100, Name = $"M{monitor} Full" });
                }
            }

            Console.WriteLine($"Created {zoneConfigs.Count} default zones across {monitorCount} monitor(s)");

            BuildZonesFromConfig();
        }

        private void BuildZonesFromConfig()
        {
            zones.Clear();
            Screen[] monitors = Screen.AllScreens;

            int skipped = 0;
            foreach (var config in zoneConfigs)
            {
                if (config.Monitor < 1 || config.Monitor > monitors.Length)
                {
                    skipped++;
                    Console.WriteLine($"Skipping zone '{config.Name}' - references Monitor {config.Monitor} which doesn't exist");
                    continue;
                }

                Rectangle screen = monitors[config.Monitor - 1].WorkingArea;
                int x = screen.X + (int)(screen.Width * config.X / 100);
                int y = screen.Y + (int)(screen.Height * config.Y / 100);
                int width = (int)(screen.Width * config.Width / 100);
                int height = (int)(screen.Height * config.Height / 100);

                zones.Add(new Rectangle(x, y, width, height));
            }

            if (skipped > 0)
            {
                Console.WriteLine($"Skipped {skipped} zone(s) referencing missing monitors");
            }

            Console.WriteLine($"Built {zones.Count} zones from {zoneConfigs.Count} configs");
        }

        private void SetupGlobalHotkey()
        {
            keyboardHook = new GlobalKeyboardHook();
            keyboardHook.KeyDown += KeyboardHook_KeyDown;

            try
            {
                keyboardHook.RegisterHotKey(HotKeyModifiers.Control | HotKeyModifiers.Shift, Keys.Oemtilde);
                for (int i = 1; i <= 9; i++)
                {
                    keyboardHook.RegisterHotKey(HotKeyModifiers.Control | HotKeyModifiers.Shift, Keys.D0 + i);
                }

                trayIcon.ShowBalloonTip(4000, "Zone Manager",
                    "Hotkeys: Ctrl+Shift+` (zones), Ctrl+Shift+1–9 (snap)\n" +
                    "Drag & Drop: Hold Ctrl, then drag any window to zones", ToolTipIcon.Info);
            }
            catch
            {
                trayIcon.ShowBalloonTip(4000, "Zone Manager",
                    "Drag & Drop: Hold Ctrl, then drag any window to zones\n" +
                    "Hotkeys unavailable (run as admin for hotkeys)", ToolTipIcon.Info);

                trayIcon.Text = "Zone Manager - Hold Ctrl, then drag windows to snap to zones";
            }
        }

        private void SetupMouseHook()
        {
            try
            {
                mouseHook = new GlobalMouseHook();
                mouseHook.MouseMove += MouseHook_MouseMove;
                mouseHook.MouseDown += MouseHook_MouseDown;
                mouseHook.MouseUp += MouseHook_MouseUp;
            }
            catch
            {
                // Optional. Continue without it.
            }
        }

        // ===============================
        // WINDOW HELPER METHODS
        // ===============================

        private IntPtr GetTopLevelWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return IntPtr.Zero;
            IntPtr root = NativeApi.GetAncestor(hWnd, NativeConstants.GA_ROOT);
            return root == IntPtr.Zero ? hWnd : root;
        }

        private string GetWindowClassName(IntPtr hWnd)
        {
            var className = new System.Text.StringBuilder(256);
            NativeApi.GetClassName(hWnd, className, className.Capacity);
            return className.ToString();
        }

        private string GetWindowTextSafe(IntPtr hWnd)
        {
            var sb = new System.Text.StringBuilder(512);
            NativeApi.GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private bool IsCtrlPressed()
        {
            return (NativeApi.GetAsyncKeyState(NativeConstants.VK_CONTROL) & 0x8000) != 0;
        }

        // ===============================
        // MOUSE HOOK HANDLERS (DRAG & DROP)
        // ===============================

        private void MouseHook_MouseDown(object sender, MouseEventArgs e)
        {
            // Don't interfere with edit mode
            if (editMode)
            {
                Console.WriteLine("MouseHook: Ignoring mouse down - edit mode active");
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                ctrlWasPressed = IsCtrlPressed();

                if ((DateTime.Now - lastDragEnd).TotalMilliseconds < 500)
                {
                    return;
                }

                if (ctrlWasPressed)
                {
                    // Get the window under the cursor at the start of the drag
                    NativeApi.POINT cursorPos;
                    NativeApi.GetCursorPos(out cursorPos);

                    IntPtr windowUnderCursor = NativeApi.WindowFromPoint(cursorPos);
                    draggedWindow = GetTopLevelWindow(windowUnderCursor);

                    // If no window under cursor, try the foreground window
                    if (draggedWindow == IntPtr.Zero)
                    {
                        draggedWindow = GetTopLevelWindow(NativeApi.GetForegroundWindow());
                    }

                    Console.WriteLine($"Mouse down - CTRL pressed, captured window: {draggedWindow}");
                    if (draggedWindow != IntPtr.Zero)
                    {
                        string className = GetWindowClassName(draggedWindow);
                        string title = GetWindowTextSafe(draggedWindow);
                        Console.WriteLine($"Captured window: {title} ({className})");
                    }
                }
            }
        }

        private void MouseHook_MouseMove(object sender, MouseEventArgs e)
        {
            // Don't interfere with edit mode
            if (editMode)
                return;

            // Only start showing zones if we have a valid window and CTRL is still pressed
            if (ctrlWasPressed && IsCtrlPressed() && !isDragSnapping && draggedWindow != IntPtr.Zero)
            {
                // Verify the window is still valid and draggable
                if (NativeApi.IsWindow(draggedWindow) && NativeApi.IsWindowVisible(draggedWindow) &&
                    !NativeApi.IsIconic(draggedWindow) && !overlayHandles.Contains(draggedWindow))
                {
                    string className = GetWindowClassName(draggedWindow);

                    // Filter out system windows
                    if (!className.Contains("Shell_TrayWnd") &&
                        !className.Contains("Shell_SecondaryTrayWnd") &&
                        !className.Contains("DV2ControlHost") &&
                        className != "Progman")
                    {
                        isDragSnapping = true;
                        ShowDragZones();
                        Console.WriteLine("=== DRAG ZONES SHOWN ===");
                        Console.WriteLine($"Window: {GetWindowTextSafe(draggedWindow)}");
                        Console.WriteLine($"Class: {className}");
                    }
                }
            }

            // Highlight zones during drag
            if (isDragSnapping)
            {
                HighlightZoneUnderMouse();
            }
        }

        private void MouseHook_MouseUp(object sender, MouseEventArgs e)
        {
            // Don't interfere with edit mode
            if (editMode)
                return;

            if (e.Button == MouseButtons.Left)
            {
                lastDragEnd = DateTime.Now;

                if (isDragSnapping && draggedWindow != IntPtr.Zero)
                {
                    Console.WriteLine("=== MOUSE UP - CHECKING FOR SNAP ===");

                    int zoneIndex = GetZoneUnderMouse();
                    Console.WriteLine($"Zone under mouse: {(zoneIndex >= 0 ? (zoneIndex + 1).ToString() : "None")}");

                    if (zoneIndex >= 0)
                    {
                        Console.WriteLine($"Valid drop detected, will snap to zone {zoneIndex + 1}");

                        // Capture the window info before the delay
                        IntPtr targetWindow = draggedWindow;
                        string windowTitle = GetWindowTextSafe(targetWindow);
                        string windowClass = GetWindowClassName(targetWindow);

                        Console.WriteLine($"Target window: {windowTitle} ({windowClass})");

                        // Use multiple timers with different delays to ensure the snap works
                        var timer1 = new Timer();
                        timer1.Interval = 100; // First attempt quickly
                        timer1.Tick += (s, ev) =>
                        {
                            timer1.Stop();
                            timer1.Dispose();

                            if (NativeApi.IsWindow(targetWindow) && NativeApi.IsWindowVisible(targetWindow))
                            {
                                Console.WriteLine("First snap attempt (100ms delay)");
                                bool success = PerformSnapOperation(targetWindow, zoneIndex);

                                if (!success)
                                {
                                    // Second attempt with longer delay
                                    var timer2 = new Timer();
                                    timer2.Interval = 300;
                                    timer2.Tick += (s2, ev2) =>
                                    {
                                        timer2.Stop();
                                        timer2.Dispose();
                                        Console.WriteLine("Second snap attempt (400ms total delay)");
                                        PerformSnapOperation(targetWindow, zoneIndex);
                                    };
                                    timer2.Start();
                                }
                            }
                        };
                        timer1.Start();
                    }
                    else
                    {
                        Console.WriteLine("No zone under mouse - no snap performed");
                    }
                }

                // Clean up drag state
                EndDragSnapping();
                ctrlWasPressed = false;
                draggedWindow = IntPtr.Zero;
            }
        }

        private void EndDragSnapping()
        {
            isDragSnapping = false;
            ctrlWasPressed = false;
            HideZones();
        }

        private void HighlightZoneUnderMouse()
        {
            NativeApi.POINT cursorPos;
            NativeApi.GetCursorPos(out cursorPos);

            for (int i = 0; i < zoneOverlays.Count; i++)
            {
                if (zoneOverlays[i] is DragZoneOverlay dragOverlay)
                {
                    bool isUnderMouse = zones[i].Contains(cursorPos.X, cursorPos.Y);
                    dragOverlay.SetHighlighted(isUnderMouse);
                }
            }
        }

        private int GetZoneUnderMouse()
        {
            NativeApi.POINT cursorPos;
            NativeApi.GetCursorPos(out cursorPos);

            // Search in reverse order to match visual z-order (last drawn = on top)
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                if (zones[i].Contains(cursorPos.X, cursorPos.Y))
                {
                    Console.WriteLine($"Zone {i + 1} found at cursor position ({cursorPos.X}, {cursorPos.Y})");
                    return i;
                }
            }
            return -1;
        }

        // ===============================
        // WINDOW SNAPPING
        // ===============================

        private bool PerformSnapOperation(IntPtr window, int zoneIndex)
        {
            // Validate inputs
            if (window == IntPtr.Zero || !NativeApi.IsWindow(window) || overlayHandles.Contains(window))
            {
                Console.WriteLine("Invalid window for snap operation");
                return false;
            }

            if (zoneIndex < 0 || zoneIndex >= zones.Count)
            {
                Console.WriteLine($"Invalid zone index: {zoneIndex}");
                return false;
            }

            Rectangle zone = zones[zoneIndex];
            string className = GetWindowClassName(window);
            string title = GetWindowTextSafe(window);

            Console.WriteLine("=== PERFORMING SNAP OPERATION ===");
            Console.WriteLine($"Window: {title}");
            Console.WriteLine($"Class: {className}");
            Console.WriteLine($"Handle: {window}");
            Console.WriteLine($"Target Zone {zoneIndex + 1}: X={zone.X}, Y={zone.Y}, W={zone.Width}, H={zone.Height}");

            // Get current window position for comparison
            NativeApi.RECT currentRect;
            if (!NativeApi.GetWindowRect(window, out currentRect))
            {
                Console.WriteLine("Failed to get current window rect");
                return false;
            }

            Rectangle currentPos = new Rectangle(currentRect.Left, currentRect.Top,
                                               currentRect.Right - currentRect.Left,
                                               currentRect.Bottom - currentRect.Top);
            Console.WriteLine($"Current Position: X={currentPos.X}, Y={currentPos.Y}, W={currentPos.Width}, H={currentPos.Height}");

            // Ensure window is visible and restored
            if (!NativeApi.IsWindowVisible(window))
            {
                Console.WriteLine("Window is not visible");
                return false;
            }

            if (NativeApi.IsIconic(window))
            {
                Console.WriteLine("Restoring minimized window...");
                NativeApi.ShowWindow(window, NativeConstants.SW_RESTORE);
                System.Threading.Thread.Sleep(100);
            }

            // Try multiple approaches to move the window
            bool success = false;

            // Method 1: SetWindowPos with specific flags
            Console.WriteLine("Attempting SetWindowPos method 1...");
            success = NativeApi.SetWindowPos(window, NativeApi.HWND_TOP, zone.X, zone.Y, zone.Width, zone.Height,
                NativeConstants.SWP_SHOWWINDOW | NativeConstants.SWP_FRAMECHANGED);
            Console.WriteLine($"SetWindowPos method 1 result: {success}");

            if (!success)
            {
                // Method 2: MoveWindow
                Console.WriteLine("Attempting MoveWindow...");
                success = NativeApi.MoveWindow(window, zone.X, zone.Y, zone.Width, zone.Height, true);
                Console.WriteLine($"MoveWindow result: {success}");
            }

            if (!success)
            {
                // Method 3: Two-step approach (move then resize)
                Console.WriteLine("Attempting two-step approach...");

                bool moveSuccess = NativeApi.SetWindowPos(window, IntPtr.Zero, zone.X, zone.Y, 0, 0,
                    NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOSIZE | NativeConstants.SWP_SHOWWINDOW);
                Console.WriteLine($"Move step: {moveSuccess}");

                System.Threading.Thread.Sleep(50);

                bool resizeSuccess = NativeApi.SetWindowPos(window, IntPtr.Zero, 0, 0, zone.Width, zone.Height,
                    NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOMOVE | NativeConstants.SWP_SHOWWINDOW);
                Console.WriteLine($"Resize step: {resizeSuccess}");

                success = moveSuccess || resizeSuccess;
            }

            // Force window to update
            NativeApi.SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0,
                NativeConstants.SWP_NOMOVE | NativeConstants.SWP_NOSIZE |
                NativeConstants.SWP_NOZORDER | NativeConstants.SWP_FRAMECHANGED);

            // Check if the operation actually worked
            System.Threading.Thread.Sleep(100);
            NativeApi.RECT finalRect;
            NativeApi.GetWindowRect(window, out finalRect);
            Rectangle finalPos = new Rectangle(finalRect.Left, finalRect.Top,
                                             finalRect.Right - finalRect.Left,
                                             finalRect.Bottom - finalRect.Top);

            Console.WriteLine($"Final Position: X={finalPos.X}, Y={finalPos.Y}, W={finalPos.Width}, H={finalPos.Height}");

            bool actuallyMoved = Math.Abs(finalPos.X - currentPos.X) > 5 ||
                                Math.Abs(finalPos.Y - currentPos.Y) > 5 ||
                                Math.Abs(finalPos.Width - currentPos.Width) > 5 ||
                                Math.Abs(finalPos.Height - currentPos.Height) > 5;

            Console.WriteLine($"Window actually moved: {actuallyMoved}");

            if (actuallyMoved)
            {
                trayIcon.ShowBalloonTip(2000, "Zone Manager",
                    $"Window snapped to Zone {zoneIndex + 1}", ToolTipIcon.Info);
                Console.WriteLine("=== SNAP SUCCESSFUL ===");
                return true;
            }
            else
            {
                trayIcon.ShowBalloonTip(3000, "Zone Manager",
                    $"Snap failed for {className}. Window may not support repositioning.", ToolTipIcon.Warning);
                Console.WriteLine("=== SNAP FAILED ===");
                return false;
            }
        }

        // ===============================
        // ZONE OVERLAY SHOW/HIDE
        // ===============================

        private void ShowDragZones()
        {
            HideZones();

            // Show all zones, not just first 9
            for (int i = 0; i < zones.Count; i++)
            {
                var overlay = new DragZoneOverlay(zones[i], (i + 1).ToString(), i);
                overlay.Show();
                zoneOverlays.Add(overlay);
                overlayHandles.Add(overlay.Handle);
            }
            zonesVisible = true;
        }

        private void ShowZones()
        {
            if (editMode)
            {
                ShowEditableZones();
                return;
            }

            HideZones();

            // Show all zones, not just first 9
            for (int i = 0; i < zones.Count; i++)
            {
                var overlay = new ZoneOverlay(zones[i], (i + 1).ToString());
                overlay.Show();
                zoneOverlays.Add(overlay);
                overlayHandles.Add(overlay.Handle);
            }
            zonesVisible = true;
        }

        private void ShowEditableZones()
        {
            HideZones();

            // Set edit mode AFTER HideZones() which resets it to false
            editMode = true;

            Console.WriteLine($"=== SHOWING EDITABLE ZONES (v{VERSION}) ===");
            Console.WriteLine($"Edit mode is now: {editMode}");
            Console.WriteLine($"Creating {zones.Count} editable overlays");

            // Show all zones, not just first 9
            for (int i = 0; i < zones.Count; i++)
            {
                Console.WriteLine($"Creating editable overlay {i + 1} at {zones[i]}");
                var overlay = new EditableZoneOverlay(zones[i], (i + 1).ToString(), i, this);
                overlay.Show();
                zoneOverlays.Add(overlay);
                overlayHandles.Add(overlay.Handle);
                Console.WriteLine($"Overlay {i + 1} created with handle: {overlay.Handle}");
            }
            zonesVisible = true;

            Console.WriteLine($"Total editable overlays created: {zoneOverlays.Count}");

            trayIcon.ShowBalloonTip(5000, "Zone Editor",
                "Drag zones to move, drag corners to resize. Right-click tray → 'Save Current Layout' when done.",
                ToolTipIcon.Info);
        }

        private void HideZones()
        {
            foreach (var overlay in zoneOverlays)
            {
                overlayHandles.Remove(overlay.Handle);
                overlay.Close();
            }
            zoneOverlays.Clear();
            overlayHandles.Clear();
            zonesVisible = false;
            editMode = false;
        }

        // ===============================
        // HOTKEYS & TRAY MENU HANDLERS
        // ===============================

        private void KeyboardHook_KeyDown(object sender, KeyPressedEventArgs e)
        {
            if (e.Modifier == (HotKeyModifiers.Control | HotKeyModifiers.Shift) && e.Key == Keys.Oemtilde)
            {
                ToggleZones();
            }
            else if (e.Modifier == (HotKeyModifiers.Control | HotKeyModifiers.Shift) && e.Key >= Keys.D1 && e.Key <= Keys.D9)
            {
                int zoneIndex = e.Key - Keys.D1;
                if (zoneIndex < zones.Count)
                {
                    SnapActiveWindowToZone(zoneIndex);
                }
            }
        }

        private void ToggleZones()
        {
            if (zonesVisible) HideZones();
            else ShowZones();
        }

        private void SnapActiveWindowToZone(int zoneIndex)
        {
            IntPtr activeWindow = GetTopLevelWindow(NativeApi.GetForegroundWindow());
            if (activeWindow != IntPtr.Zero &&
                !overlayHandles.Contains(activeWindow) &&
                NativeApi.IsWindowVisible(activeWindow) && !NativeApi.IsIconic(activeWindow))
            {
                Rectangle zone = zones[zoneIndex];
                NativeApi.SetWindowPos(activeWindow, IntPtr.Zero,
                    zone.X, zone.Y, zone.Width, zone.Height,
                    NativeConstants.SWP_NOZORDER | NativeConstants.SWP_SHOWWINDOW);
            }
            HideZones();
        }

        private void TrayIcon_DoubleClick(object sender, EventArgs e) => ToggleZones();
        private void ShowZones_Click(object sender, EventArgs e) => ShowZones();
        private void HideZones_Click(object sender, EventArgs e) => HideZones();

        private void TestSnapActive_Click(object sender, EventArgs e)
        {
            IntPtr activeWindow = GetTopLevelWindow(NativeApi.GetForegroundWindow());
            if (activeWindow != IntPtr.Zero && !overlayHandles.Contains(activeWindow))
            {
                string className = GetWindowClassName(activeWindow);
                var result = MessageBox.Show(
                    $"Test snapping the active window?\n\n" +
                    $"Window: {className}\n" +
                    $"Handle: {activeWindow}\n\n" +
                    $"This will snap to Zone 1 for testing purposes.",
                    "Test Window Snapping",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    Console.WriteLine("=== MANUAL SNAP TEST ===");
                    PerformSnapOperation(activeWindow, 0);
                }
            }
            else
            {
                MessageBox.Show("No active window found!", "Test Snap", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void EditZones_Click(object sender, EventArgs e)
        {
            Console.WriteLine($"=== EDIT ZONES CLICKED (v{VERSION}) ===");
            Console.WriteLine($"Current zones count: {zones.Count}");

            if (zones.Count == 0)
            {
                MessageBox.Show(
                    "No zones available to edit!\n\n" +
                    "This might happen if your monitor configuration has changed.\n" +
                    "Try 'Reset to Defaults' from the tray menu.",
                    "No Zones Available",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // editMode is set inside ShowEditableZones() after HideZones()
            ShowEditableZones();
        }

        private void SaveZones_Click(object sender, EventArgs e)
        {
            SaveZoneConfig();
            trayIcon.ShowBalloonTip(2000, "Zone Manager", "Layout saved successfully!", ToolTipIcon.Info);
        }

        private void ReloadConfig_Click(object sender, EventArgs e)
        {
            HideZones();
            LoadZoneConfigWithMonitorCheck();
            trayIcon.ShowBalloonTip(2000, "Zone Manager", $"Config reloaded. {zones.Count} zones loaded.", ToolTipIcon.Info);
        }

        private void ResetZones_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show("Reset to default zone layout? This will delete your custom zones.",
                "Reset Zones", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                CreateDefaultZones();
                SaveZoneConfig();
                HideZones();
                trayIcon.ShowBalloonTip(2000, "Zone Manager", "Zones reset to defaults!", ToolTipIcon.Info);
            }
        }

        private void ShowMonitorInfo_Click(object sender, EventArgs e)
        {
            string info = MonitorManager.GetMonitorInfo();
            info += "\nUse these monitor numbers in your zone configurations!";

            MessageBox.Show(info, "Monitor Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void Exit_Click(object sender, EventArgs e)
        {
            keyboardHook?.Dispose();
            mouseHook?.Dispose();
            trayIcon.Visible = false;
            Application.Exit();
        }

        private void RestartAsAdmin_Click(object sender, EventArgs e)
        {
            try
            {
                var exePath = Application.ExecutablePath;
                var startInfo = new ProcessStartInfo(exePath);
                startInfo.Verb = "runas";
                Process.Start(startInfo);
                Exit_Click(sender, e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not restart as administrator: {ex.Message}", "Restart Failed");
            }
        }

        private void OpenConfigFolder_Click(object sender, EventArgs e)
        {
            try
            {
                string configDir = Path.GetDirectoryName(configPath);
                Process.Start("explorer.exe", configDir);

                string info = $"Config folder opened!\n\n" +
                              $"Main config: ZoneConfig.xml\n" +
                              $"Backups: ZoneConfig.xml.backup.*\n\n" +
                              $"If you lost your zones, look for backup files.\n" +
                              $"You can rename a backup to ZoneConfig.xml to restore it.";
                MessageBox.Show(info, "Config Folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open config folder: {ex.Message}\n\nPath: {configPath}", "Error");
            }
        }

        private void ShowConsole_Click(object sender, EventArgs e)
        {
            IntPtr consoleWindow = NativeApi.GetConsoleWindow();

            if (consoleWindow == IntPtr.Zero)
            {
                NativeApi.AllocConsole();
                Console.WriteLine("=== Zone Manager Debug Console ===");
                Console.WriteLine("Debug output will appear here during drag operations.");
                Console.WriteLine("Close this window or restart the app to hide console.\n");
            }
            else
            {
                NativeApi.ShowWindow(consoleWindow, NativeConstants.SW_SHOW);
            }
        }

        private void PinInstructions_Click(object sender, EventArgs e)
        {
            string instructions =
                $"LIGHTWEIGHT ZONE MANAGER v{VERSION}\n" +
                $"Build Date: {BUILD_DATE}\n" +
                $"═══════════════════════════════════════\n\n" +
                "HOW TO USE ZONE MANAGER:\n\n" +
                "DRAG & DROP SNAPPING (No Admin Required):\n" +
                "• Hold CTRL, then START dragging any window\n" +
                "• Keep CTRL held while dragging\n" +
                "• Zones will appear after you start moving\n" +
                "• Drag over a zone to highlight it\n" +
                "• Release mouse (while still in zone) to snap\n\n" +
                "HOTKEYS (Admin Mode Only):\n" +
                "• Ctrl+Shift+` = Show/hide zones\n" +
                "• Ctrl+Shift+1–9 = Snap active window to zone 1-9\n" +
                "  (Zones 10+ work with drag & drop only)\n\n" +
                "EDIT ZONES:\n" +
                "• Right-click tray icon → 'Edit Zones'\n" +
                "• Drag zones to move them\n" +
                "• Drag corners/edges to resize\n" +
                "• Right-click tray → 'Save Current Layout' when done\n\n" +
                "ADD MORE ZONES:\n" +
                "• Edit ZoneConfig.xml manually (right-click → Open Config Folder)\n" +
                "• Each zone needs: Monitor, X, Y, Width, Height (as percentages)\n" +
                "• Or use 'Reset to Defaults' to auto-generate for all monitors\n\n" +
                "SYSTEM INFO:\n" +
                $"• Monitors: {MonitorManager.GetMonitorCount()}\n" +
                $"• Zones loaded: {zones.Count}\n" +
                $"• Config: {Path.GetFileName(configPath)}\n" +
                "• Elevated windows require running as Admin\n\n" +
                "NEW IN v2.3: Unlimited zones & multi-monitor support!";
            MessageBox.Show(instructions, "Zone Manager - Usage Instructions", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }

        public void UpdateZoneFromEdit(int zoneIndex, Rectangle newBounds)
        {
            // Guard against bad indexes
            if (zoneIndex < 0 || zoneIndex >= zones.Count || zoneIndex >= zoneConfigs.Count)
                return;

            // Update the in-memory rectangle used by overlays
            zones[zoneIndex] = newBounds;

            // Also update the saved config (percentages relative to the monitor's working area)
            Screen[] monitors = Screen.AllScreens;
            var config = zoneConfigs[zoneIndex];

            if (config.Monitor >= 1 && config.Monitor <= monitors.Length)
            {
                Rectangle screen = monitors[config.Monitor - 1].WorkingArea;

                config.X = ((double)(newBounds.X - screen.X) / screen.Width) * 100.0;
                config.Y = ((double)(newBounds.Y - screen.Y) / screen.Height) * 100.0;
                config.Width = ((double)newBounds.Width / screen.Width) * 100.0;
                config.Height = ((double)newBounds.Height / screen.Height) * 100.0;
            }
        }
    }
}
