using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.IO;
using System.Xml.Serialization;
using System.Diagnostics;

namespace LightweightZoneManager
{
    // -------------------------------
    // Shared native constants (available to all classes in this file)
    // -------------------------------
    internal static class NativeConstants
    {
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public const int SW_RESTORE = 9;
        public const int SW_SHOW = 5;

        public const int VK_CONTROL = 0x11;
        public const int VK_LBUTTON = 0x01;

        public const uint GA_PARENT = 1;
        public const uint GA_ROOT = 2;
        public const uint GA_ROOTOWNER = 3;
    }

    // Zone configuration class for saving/loading
    [Serializable]
    public class ZoneConfig
    {
        public int Monitor { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Name { get; set; }
    }

    [Serializable]
    public class ZoneSettings
    {
        public List<ZoneConfig> Zones { get; set; } = new List<ZoneConfig>();
        public int Version { get; set; } = 1;
    }

    public partial class ZoneManager : Form
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private bool zonesVisible = false;
        private readonly List<Rectangle> zones = new List<Rectangle>();
        private readonly List<Form> zoneOverlays = new List<Form>();
        private readonly HashSet<IntPtr> overlayHandles = new HashSet<IntPtr>(); // track overlay HWNDs

        private GlobalKeyboardHook keyboardHook;
        private GlobalMouseHook mouseHook;
        private string configPath;
        private bool editMode = false;
        private List<ZoneConfig> zoneConfigs = new List<ZoneConfig>();
        private bool isDragSnapping = false;
        private IntPtr draggedWindow = IntPtr.Zero;
        private bool ctrlWasPressed = false;
        private DateTime lastDragEnd = DateTime.MinValue;

        // Windows API imports
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        private static readonly IntPtr HWND_TOP = new IntPtr(0);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left; public int Top; public int Right; public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X; public int Y;
        }

        public ZoneManager()
        {
            InitializeComponent();

            // Set up config file path in the same directory as the executable
            configPath = Path.Combine(Application.StartupPath, "ZoneConfig.xml");

            SetupTrayIcon();
            LoadZoneConfig();
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
                Text = "Lightweight Zone Manager - Ctrl+Shift+` to show zones"
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

        private void LoadZoneConfig()
        {
            try
            {
                Console.WriteLine($"Looking for config file at: {configPath}");

                if (File.Exists(configPath))
                {
                    Console.WriteLine("Config file found, attempting to load...");

                    string xmlContent = File.ReadAllText(configPath);
                    Console.WriteLine($"Config file content length: {xmlContent.Length} characters");

                    if (xmlContent.Length < 50)
                    {
                        Console.WriteLine("Config file appears to be corrupted (too short), using defaults");
                        CreateDefaultZones();
                        SaveZoneConfig();
                        return;
                    }

                    XmlSerializer serializer = new XmlSerializer(typeof(ZoneSettings));
                    using (StringReader reader = new StringReader(xmlContent))
                    {
                        var settings = (ZoneSettings)serializer.Deserialize(reader);
                        zoneConfigs = settings.Zones ?? new List<ZoneConfig>();

                        Console.WriteLine($"Loaded {zoneConfigs.Count} zones from config");

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
                }
                else
                {
                    Console.WriteLine("Config file not found, creating defaults");
                    CreateDefaultZones();
                    SaveZoneConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading zone config: {ex.Message}");
                MessageBox.Show($"Error loading zone config: {ex.Message}\n\nUsing default zones. Your previous zones may be in the backup.", "Config Error");

                try
                {
                    if (File.Exists(configPath))
                    {
                        string backupPath = configPath + ".backup." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        File.Copy(configPath, backupPath);
                        Console.WriteLine($"Corrupted config backed up to: {backupPath}");
                    }
                }
                catch (Exception backupEx)
                {
                    Console.WriteLine($"Could not backup corrupted config: {backupEx.Message}");
                }

                CreateDefaultZones();
            }
        }

        private void SaveZoneConfig()
        {
            try
            {
                Console.WriteLine($"Saving {zoneConfigs.Count} zones to: {configPath}");

                var settings = new ZoneSettings { Zones = zoneConfigs };
                XmlSerializer serializer = new XmlSerializer(typeof(ZoneSettings));

                using (FileStream stream = new FileStream(configPath, FileMode.Create))
                {
                    serializer.Serialize(stream, settings);
                }

                if (File.Exists(configPath))
                {
                    var fileInfo = new FileInfo(configPath);
                    Console.WriteLine($"Config saved successfully. File size: {fileInfo.Length} bytes");
                }
                else
                {
                    Console.WriteLine("ERROR: Config file was not created!");
                }
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
            Screen[] monitors = Screen.AllScreens;

            if (monitors.Length >= 1)
            {
                zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 0, Y = 0, Width = 50, Height = 50, Name = "Top-Left" });
                zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 50, Y = 0, Width = 50, Height = 50, Name = "Top-Right" });
                zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 0, Y = 50, Width = 50, Height = 50, Name = "Bottom-Left" });
                zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 50, Y = 50, Width = 50, Height = 50, Name = "Bottom-Right" });
                zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 0, Y = 0, Width = 50, Height = 100, Name = "Left Half" });
                zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 50, Y = 0, Width = 50, Height = 100, Name = "Right Half" });
            }

            if (monitors.Length >= 2)
            {
                zoneConfigs.Add(new ZoneConfig { Monitor = 2, X = 0, Y = 0, Width = 100, Height = 50, Name = "Monitor 2 Top" });
                zoneConfigs.Add(new ZoneConfig { Monitor = 2, X = 0, Y = 50, Width = 100, Height = 50, Name = "Monitor 2 Bottom" });
                zoneConfigs.Add(new ZoneConfig { Monitor = 2, X = 0, Y = 0, Width = 100, Height = 100, Name = "Monitor 2 Full" });
            }

            BuildZonesFromConfig();
        }

        private void BuildZonesFromConfig()
        {
            zones.Clear();
            Screen[] monitors = Screen.AllScreens;

            foreach (var config in zoneConfigs)
            {
                if (config.Monitor < 1 || config.Monitor > monitors.Length) continue;

                Rectangle screen = monitors[config.Monitor - 1].WorkingArea;
                int x = screen.X + (int)(screen.Width * config.X / 100);
                int y = screen.Y + (int)(screen.Height * config.Y / 100);
                int width = (int)(screen.Width * config.Width / 100);
                int height = (int)(screen.Height * config.Height / 100);

                zones.Add(new Rectangle(x, y, width, height));
            }
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

        private IntPtr GetTopLevelWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return IntPtr.Zero;
            IntPtr root = GetAncestor(hWnd, NativeConstants.GA_ROOT);
            return root == IntPtr.Zero ? hWnd : root;
        }

        private string GetWindowClassName(IntPtr hWnd)
        {
            var className = new System.Text.StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);
            return className.ToString();
        }

        private string GetWindowTextSafe(IntPtr hWnd)
        {
            var sb = new System.Text.StringBuilder(512);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private bool IsCtrlPressed()
        {
            return (GetAsyncKeyState(NativeConstants.VK_CONTROL) & 0x8000) != 0;
        }

        // -------------------------------
        // Mouse Hook Handlers
        // -------------------------------
        private void MouseHook_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ctrlWasPressed = IsCtrlPressed();

                if ((DateTime.Now - lastDragEnd).TotalMilliseconds < 500)
                {
                    return;
                }

                if (ctrlWasPressed)
                {
                    POINT cursorPos;
                    GetCursorPos(out cursorPos);

                    IntPtr raw = WindowFromPoint(cursorPos);
                    draggedWindow = GetTopLevelWindow(raw);

                    if (draggedWindow == IntPtr.Zero)
                    {
                        draggedWindow = GetTopLevelWindow(GetForegroundWindow());
                    }
                }
            }
        }

        private void MouseHook_MouseMove(object sender, MouseEventArgs e)
        {
            if (ctrlWasPressed && IsCtrlPressed() && !isDragSnapping)
            {
                // Foreground during a drag is generally the dragged window
                IntPtr currentWindow = GetTopLevelWindow(GetForegroundWindow());
                if (currentWindow != IntPtr.Zero &&
                    !overlayHandles.Contains(currentWindow) &&
                    IsWindow(currentWindow) &&
                    IsWindowVisible(currentWindow) &&
                    !IsIconic(currentWindow))
                {
                    draggedWindow = currentWindow;
                }

                if (draggedWindow != IntPtr.Zero &&
                    IsWindowVisible(draggedWindow) &&
                    !IsIconic(draggedWindow) &&
                    !overlayHandles.Contains(draggedWindow))
                {
                    string className = GetWindowClassName(draggedWindow);
                    if (!className.Contains("Shell_TrayWnd") &&
                        !className.Contains("Shell_SecondaryTrayWnd") &&
                        !className.Contains("DV2ControlHost") &&
                        className != "Progman")
                    {
                        isDragSnapping = true;
                        ShowDragZones();
                        Console.WriteLine("=== DRAG DETECTED ===");
                        Console.WriteLine($"Zones shown for: {className}");
                        Console.WriteLine($"Window Handle: {draggedWindow}");
                        Console.WriteLine($"Window Visible: {IsWindowVisible(draggedWindow)}");
                        Console.WriteLine($"Window Iconic: {IsIconic(draggedWindow)}");
                    }
                }
            }

            if (isDragSnapping)
            {
                HighlightZoneUnderMouse();
            }
        }

        private void MouseHook_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                lastDragEnd = DateTime.Now;

                if (isDragSnapping)
                {
                    Console.WriteLine("=== MOUSE UP DETECTED ===");
                    Console.WriteLine("Drag was active, checking for zone under mouse...");

                    int zoneIndex = GetZoneUnderMouse();
                    Console.WriteLine($"Zone under mouse: {zoneIndex}");
                    Console.WriteLine($"Dragged window handle: {draggedWindow}");

                    if (zoneIndex >= 0 && draggedWindow != IntPtr.Zero)
                    {
                        Console.WriteLine($"Valid drop detected, scheduling snap to zone {zoneIndex + 1}");

                        var snapTimer = new Timer();
                        snapTimer.Interval = 280; // slightly longer to avoid fighting the OS drag loop
                        snapTimer.Tick += (s, ev) =>
                        {
                            snapTimer.Stop();
                            snapTimer.Dispose();
                            PerformDelayedSnap(draggedWindow, zoneIndex);
                        };
                        snapTimer.Start();

                        Console.WriteLine($"Delayed snap queued for zone {zoneIndex + 1}");
                    }
                    else
                    {
                        Console.WriteLine("Invalid drop - no zone or no window");
                    }

                    EndDragSnapping();
                }

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
            POINT cursorPos;
            GetCursorPos(out cursorPos);

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
            POINT cursorPos;
            GetCursorPos(out cursorPos);

            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i].Contains(cursorPos.X, cursorPos.Y))
                {
                    return i;
                }
            }
            return -1;
        }

        private void PerformDelayedSnap(IntPtr window, int zoneIndex)
        {
            window = GetTopLevelWindow(window);

            if (window == IntPtr.Zero || !IsWindow(window) || overlayHandles.Contains(window))
            {
                Console.WriteLine("Invalid target (null/dead/overlay). Aborting snap.");
                return;
            }

            if (zoneIndex < 0 || zoneIndex >= zones.Count) return;

            Rectangle zone = zones[zoneIndex];
            string className = GetWindowClassName(window);
            string title = GetWindowTextSafe(window);

            Console.WriteLine("=== ATTEMPTING SNAP ===");
            Console.WriteLine($"Window Title: {title}");
            Console.WriteLine($"Window Class: {className}");
            Console.WriteLine($"Handle: {window}");
            Console.WriteLine($"Target Zone {zoneIndex + 1}: {zone}");
            Console.WriteLine($"Window Visible: {IsWindowVisible(window)}");
            Console.WriteLine($"Window Iconic: {IsIconic(window)}");

            // current pos
            RECT currentRect;
            GetWindowRect(window, out currentRect);
            Rectangle currentPos = new Rectangle(currentRect.Left, currentRect.Top,
                                                currentRect.Right - currentRect.Left,
                                                currentRect.Bottom - currentRect.Top);
            Console.WriteLine($"Current Position: {currentPos}");

            if (IsWindowVisible(window))
            {
                if (IsIconic(window))
                {
                    Console.WriteLine("Restoring minimized window...");
                    ShowWindow(window, NativeConstants.SW_RESTORE);
                    System.Threading.Thread.Sleep(200);
                }

                Console.WriteLine("Trying MoveWindow...");
                bool success1 = MoveWindow(window, zone.X, zone.Y, zone.Width, zone.Height, true);
                Console.WriteLine($"MoveWindow result: {success1}");

                if (!success1)
                {
                    Console.WriteLine("MoveWindow failed, trying SetWindowPos method 1...");
                    bool success2 = SetWindowPos(window, IntPtr.Zero,
                        zone.X, zone.Y, zone.Width, zone.Height,
                        NativeConstants.SWP_NOZORDER | NativeConstants.SWP_SHOWWINDOW);
                    Console.WriteLine($"SetWindowPos method 1 result: {success2}");

                    if (!success2)
                    {
                        Console.WriteLine("Method 1 failed, trying SetWindowPos method 2...");
                        bool success3 = SetWindowPos(window, HWND_TOP,
                            zone.X, zone.Y, zone.Width, zone.Height,
                            NativeConstants.SWP_SHOWWINDOW);
                        Console.WriteLine($"SetWindowPos method 2 result: {success3}");

                        if (!success3)
                        {
                            Console.WriteLine("Method 2 failed, trying two-step approach...");

                            bool moveSuccess = SetWindowPos(window, IntPtr.Zero,
                                zone.X, zone.Y, 0, 0,
                                NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOSIZE | NativeConstants.SWP_SHOWWINDOW);
                            Console.WriteLine($"Move step result: {moveSuccess}");

                            System.Threading.Thread.Sleep(100);

                            bool resizeSuccess = SetWindowPos(window, IntPtr.Zero,
                                0, 0, zone.Width, zone.Height,
                                NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOMOVE | NativeConstants.SWP_SHOWWINDOW);
                            Console.WriteLine($"Resize step result: {resizeSuccess}");
                        }
                    }
                }

                // Force frame refresh
                SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0,
                    NativeConstants.SWP_NOMOVE | NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOZORDER | NativeConstants.SWP_FRAMECHANGED);

                System.Threading.Thread.Sleep(100);
                GetWindowRect(window, out currentRect);
                Rectangle finalPos = new Rectangle(currentRect.Left, currentRect.Top,
                                                   currentRect.Right - currentRect.Left,
                                                   currentRect.Bottom - currentRect.Top);
                Console.WriteLine($"Final Position: {finalPos}");

                bool actuallyMoved = (finalPos.X != currentPos.X || finalPos.Y != currentPos.Y ||
                                      finalPos.Width != currentPos.Width || finalPos.Height != currentPos.Height);
                Console.WriteLine($"Window actually moved: {actuallyMoved}");

                if (actuallyMoved)
                {
                    trayIcon.ShowBalloonTip(2000, "Zone Manager",
                        $"Window snapped to Zone {zoneIndex + 1}", ToolTipIcon.Info);
                }
                else
                {
                    trayIcon.ShowBalloonTip(2000, "Zone Manager",
                        $"Snap may have failed for {className}. See console for details.", ToolTipIcon.Warning);
                }
            }
            else
            {
                Console.WriteLine("Window is not visible, skipping snap");
            }

            Console.WriteLine("=== SNAP ATTEMPT COMPLETE ===\n");
        }

        // -------------------------------
        // Zone overlay show/hide
        // -------------------------------
        private void ShowDragZones()
        {
            HideZones();

            for (int i = 0; i < zones.Count && i < 9; i++)
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

            for (int i = 0; i < zones.Count && i < 9; i++)
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

            for (int i = 0; i < zones.Count && i < 9; i++)
            {
                var overlay = new EditableZoneOverlay(zones[i], (i + 1).ToString(), i, this);
                overlay.Show();
                zoneOverlays.Add(overlay);
                overlayHandles.Add(overlay.Handle);
            }
            zonesVisible = true;

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

        // -------------------------------
        // Hotkeys / Tray
        // -------------------------------
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
            IntPtr activeWindow = GetTopLevelWindow(GetForegroundWindow());
            if (activeWindow != IntPtr.Zero &&
                !overlayHandles.Contains(activeWindow) &&
                IsWindowVisible(activeWindow) && !IsIconic(activeWindow))
            {
                Rectangle zone = zones[zoneIndex];
                SetWindowPos(activeWindow, IntPtr.Zero,
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
            IntPtr activeWindow = GetTopLevelWindow(GetForegroundWindow());
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
                    PerformDelayedSnap(activeWindow, 0);
                }
            }
            else
            {
                MessageBox.Show("No active window found!", "Test Snap", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void EditZones_Click(object sender, EventArgs e)
        {
            editMode = true;
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
            LoadZoneConfig();
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
            Screen[] monitors = Screen.AllScreens;
            string info = $"Detected {monitors.Length} monitor(s):\n\n";

            for (int i = 0; i < monitors.Length; i++)
            {
                var monitor = monitors[i];
                info += $"Monitor {i + 1}:\n";
                info += $"  Resolution: {monitor.Bounds.Width} x {monitor.Bounds.Height}\n";
                info += $"  Position: ({monitor.Bounds.X}, {monitor.Bounds.Y})\n";
                info += $"  Primary: {(monitor.Primary ? "Yes" : "No")}\n";
                info += $"  Device: {monitor.DeviceName}\n\n";
            }

            info += "Use these monitor numbers in your AddZone() calls!\n";
            info += "Example: AddZone(2, 0, 0, 50, 100) = Left half of Monitor 2";

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
            IntPtr consoleWindow = GetConsoleWindow();

            if (consoleWindow == IntPtr.Zero)
            {
                AllocConsole();
                Console.WriteLine("=== Zone Manager Debug Console ===");
                Console.WriteLine("Debug output will appear here during drag operations.");
                Console.WriteLine("Close this window or restart the app to hide console.\n");
            }
            else
            {
                ShowWindow(consoleWindow, NativeConstants.SW_SHOW);
            }
        }

        private void PinInstructions_Click(object sender, EventArgs e)
        {
            string instructions =
                "HOW TO USE ZONE MANAGER:\n\n" +
                "DRAG & DROP SNAPPING (No Admin Required):\n" +
                "• Hold CTRL, then START dragging any window\n" +
                "• Keep CTRL held while dragging\n" +
                "• Zones will appear after you start moving\n" +
                "• Drag over a zone to highlight it\n" +
                "• Release mouse (while still in zone) to snap\n\n" +
                "HOTKEYS (Admin Mode Only):\n" +
                "• Ctrl+Shift+` = Show/hide zones\n" +
                "• Ctrl+Shift+1–9 = Snap active window to zone\n\n" +
                "TROUBLESHOOTING:\n" +
                $"• Config: {configPath}\n" +
                $"• Zones loaded: {zones.Count}\n" +
                "• Elevated windows require running this app as Admin.";
            MessageBox.Show(instructions, "Zone Manager Usage & Pin Instructions", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }
        // Add this inside the ZoneManager class, e.g., right after ShowEditableZones()
        public void UpdateZoneFromEdit(int zoneIndex, Rectangle newBounds)
        {
            // Guard against bad indexes
            if (zoneIndex < 0 || zoneIndex >= zones.Count || zoneIndex >= zoneConfigs.Count)
                return;

            // Update the in-memory rectangle used by overlays
            zones[zoneIndex] = newBounds;

            // Also update the saved config (percentages relative to the monitor’s working area)
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

    // -------------------------------
    // Zone overlay form (view-only)
    // -------------------------------
    public class ZoneOverlay : Form
    {
        protected override bool ShowWithoutActivation => true;
        private readonly string zoneNumber;

        private static readonly Color[] zoneColors = new Color[]
        {
            Color.Blue, Color.Red, Color.Green, Color.Orange, Color.Purple,
            Color.Yellow, Color.Magenta, Color.Cyan, Color.Pink
        };

        public ZoneOverlay(Rectangle bounds, string number)
        {
            zoneNumber = number;

            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = bounds;

            int colorIndex = (int.Parse(number) - 1) % zoneColors.Length;
            this.BackColor = zoneColors[colorIndex];
            this.Opacity = 0.7;

            var timer = new Timer();
            timer.Interval = 8000;
            timer.Tick += (s, e) => { this.Close(); timer.Dispose(); };
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var borderPen = new Pen(Color.White, 4))
            {
                e.Graphics.DrawRectangle(borderPen, 2, 2, this.Width - 4, this.Height - 4);
            }
            using (var backgroundBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            using (var textBrush = new SolidBrush(Color.White))
            using (var font = new Font("Arial", 32, FontStyle.Bold))
            {
                var size = e.Graphics.MeasureString(zoneNumber, font);
                var point = new PointF((this.Width - size.Width) / 2, (this.Height - size.Height) / 2);
                var textRect = new RectangleF(point.X - 10, point.Y - 5, size.Width + 20, size.Height + 10);
                e.Graphics.FillRectangle(backgroundBrush, textRect);
                e.Graphics.DrawString(zoneNumber, font, textBrush, point);
            }
            using (var infoBrush = new SolidBrush(Color.White))
            using (var infoFont = new Font("Arial", 9, FontStyle.Bold))
            {
                string info = $"Zone {zoneNumber}\n{this.Width}×{this.Height}";
                e.Graphics.DrawString(info, infoFont, infoBrush, new PointF(8, 8));
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeConstants.WS_EX_LAYERED;
                cp.ExStyle |= NativeConstants.WS_EX_TRANSPARENT;
                cp.ExStyle |= NativeConstants.WS_EX_NOACTIVATE;
                return cp;
            }
        }
    }

    // -------------------------------
    // Editable zone overlay (drag/resize)
    // -------------------------------
    public class EditableZoneOverlay : Form
    {
        protected override bool ShowWithoutActivation => true;

        private readonly string zoneNumber;
        private readonly int zoneIndex;
        private readonly ZoneManager parentManager;

        private bool isDragging = false;
        private bool isResizing = false;
        private Point dragOffset;
        private ResizeDirection resizeDirection;

        private enum ResizeDirection
        {
            None, TopLeft, TopRight, BottomLeft, BottomRight,
            Left, Right, Top, Bottom, Move
        }

        private static readonly Color[] zoneColors = new Color[]
        {
            Color.Blue, Color.Red, Color.Green, Color.Orange, Color.Purple,
            Color.Yellow, Color.Magenta, Color.Cyan, Color.Pink
        };

        public EditableZoneOverlay(Rectangle bounds, string number, int index, ZoneManager manager)
        {
            zoneNumber = number;
            zoneIndex = index;
            parentManager = manager;

            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = bounds;

            int colorIndex = (int.Parse(number) - 1) % zoneColors.Length;
            this.BackColor = zoneColors[colorIndex];
            this.Opacity = 0.8;

            this.SetStyle(ControlStyles.UserMouse, true);
            this.MouseDown += OnMouseDown;
            this.MouseMove += OnMouseMove;
            this.MouseUp += OnMouseUp;
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                resizeDirection = GetResizeDirection(e.Location);

                if (resizeDirection == ResizeDirection.Move)
                {
                    isDragging = true;
                    dragOffset = e.Location;
                }
                else if (resizeDirection != ResizeDirection.None)
                {
                    isResizing = true;
                    dragOffset = e.Location;
                }

                this.Capture = true;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                Point newLocation = new Point(
                    this.Location.X + e.X - dragOffset.X,
                    this.Location.Y + e.Y - dragOffset.Y
                );
                this.Location = newLocation;
            }
            else if (isResizing)
            {
                ResizeZone(e.Location);
            }
            else
            {
                ResizeDirection direction = GetResizeDirection(e.Location);
                this.Cursor = GetCursorForDirection(direction);
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (isDragging || isResizing)
            {
                parentManager.UpdateZoneFromEdit(zoneIndex, this.Bounds);
                isDragging = false;
                isResizing = false;
                this.Capture = false;
                this.Cursor = Cursors.Default;
            }
        }

        private ResizeDirection GetResizeDirection(Point point)
        {
            const int handleSize = 10;

            bool nearLeft = point.X <= handleSize;
            bool nearRight = point.X >= this.Width - handleSize;
            bool nearTop = point.Y <= handleSize;
            bool nearBottom = point.Y >= this.Height - handleSize;

            if (nearTop && nearLeft) return ResizeDirection.TopLeft;
            if (nearTop && nearRight) return ResizeDirection.TopRight;
            if (nearBottom && nearLeft) return ResizeDirection.BottomLeft;
            if (nearBottom && nearRight) return ResizeDirection.BottomRight;
            if (nearLeft) return ResizeDirection.Left;
            if (nearRight) return ResizeDirection.Right;
            if (nearTop) return ResizeDirection.Top;
            if (nearBottom) return ResizeDirection.Bottom;

            return ResizeDirection.Move;
        }

        private Cursor GetCursorForDirection(ResizeDirection direction)
        {
            switch (direction)
            {
                case ResizeDirection.TopLeft:
                case ResizeDirection.BottomRight:
                    return Cursors.SizeNWSE;
                case ResizeDirection.TopRight:
                case ResizeDirection.BottomLeft:
                    return Cursors.SizeNESW;
                case ResizeDirection.Left:
                case ResizeDirection.Right:
                    return Cursors.SizeWE;
                case ResizeDirection.Top:
                case ResizeDirection.Bottom:
                    return Cursors.SizeNS;
                case ResizeDirection.Move:
                    return Cursors.SizeAll;
                default:
                    return Cursors.Default;
            }
        }

        private void ResizeZone(Point mousePoint)
        {
            Rectangle newBounds = this.Bounds;

            switch (resizeDirection)
            {
                case ResizeDirection.TopLeft:
                    newBounds = new Rectangle(
                        this.Location.X + mousePoint.X - dragOffset.X,
                        this.Location.Y + mousePoint.Y - dragOffset.Y,
                        this.Width - (mousePoint.X - dragOffset.X),
                        this.Height - (mousePoint.Y - dragOffset.Y)
                    );
                    break;
                case ResizeDirection.TopRight:
                    newBounds = new Rectangle(
                        this.Location.X,
                        this.Location.Y + mousePoint.Y - dragOffset.Y,
                        mousePoint.X,
                        this.Height - (mousePoint.Y - dragOffset.Y)
                    );
                    break;
                case ResizeDirection.BottomLeft:
                    newBounds = new Rectangle(
                        this.Location.X + mousePoint.X - dragOffset.X,
                        this.Location.Y,
                        this.Width - (mousePoint.X - dragOffset.X),
                        mousePoint.Y
                    );
                    break;
                case ResizeDirection.BottomRight:
                    newBounds = new Rectangle(
                        this.Location.X,
                        this.Location.Y,
                        mousePoint.X,
                        mousePoint.Y
                    );
                    break;
                case ResizeDirection.Left:
                    newBounds = new Rectangle(
                        this.Location.X + mousePoint.X - dragOffset.X,
                        this.Location.Y,
                        this.Width - (mousePoint.X - dragOffset.X),
                        this.Height
                    );
                    break;
                case ResizeDirection.Right:
                    newBounds = new Rectangle(
                        this.Location.X,
                        this.Location.Y,
                        mousePoint.X,
                        this.Height
                    );
                    break;
                case ResizeDirection.Top:
                    newBounds = new Rectangle(
                        this.Location.X,
                        this.Location.Y + mousePoint.Y - dragOffset.Y,
                        this.Width,
                        this.Height - (mousePoint.Y - dragOffset.Y)
                    );
                    break;
                case ResizeDirection.Bottom:
                    newBounds = new Rectangle(
                        this.Location.X,
                        this.Location.Y,
                        this.Width,
                        mousePoint.Y
                    );
                    break;
            }

            if (newBounds.Width >= 50 && newBounds.Height >= 30)
            {
                this.Bounds = newBounds;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            using (var borderPen = new Pen(Color.White, 3))
            {
                e.Graphics.DrawRectangle(borderPen, 1, 1, this.Width - 2, this.Height - 2);
            }

            const int handleSize = 8;
            using (var handleBrush = new SolidBrush(Color.White))
            {
                e.Graphics.FillRectangle(handleBrush, 0, 0, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, this.Width - handleSize, 0, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, 0, this.Height - handleSize, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, this.Width - handleSize, this.Height - handleSize, handleSize, handleSize);

                e.Graphics.FillRectangle(handleBrush, this.Width / 2 - handleSize / 2, 0, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, this.Width / 2 - handleSize / 2, this.Height - handleSize, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, 0, this.Height / 2 - handleSize / 2, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, this.Width - handleSize, this.Height / 2 - handleSize / 2, handleSize, handleSize);
            }

            using (var backgroundBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            using (var textBrush = new SolidBrush(Color.White))
            using (var font = new Font("Arial", 24, FontStyle.Bold))
            using (var smallFont = new Font("Arial", 9, FontStyle.Bold))
            {
                var size = e.Graphics.MeasureString(zoneNumber, font);
                var point = new PointF((this.Width - size.Width) / 2, (this.Height - size.Height) / 2);

                var textRect = new RectangleF(point.X - 10, point.Y - 5, size.Width + 20, size.Height + 10);
                e.Graphics.FillRectangle(backgroundBrush, textRect);

                e.Graphics.DrawString(zoneNumber, font, textBrush, point);

                string instructions = "Drag to move • Drag corners/edges to resize";
                e.Graphics.DrawString(instructions, smallFont, textBrush, new PointF(10, this.Height - 25));
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeConstants.WS_EX_LAYERED;
                // Intentionally NOT transparent here because we need to interact with it
                cp.ExStyle |= NativeConstants.WS_EX_NOACTIVATE;
                return cp;
            }
        }
    }

    // -------------------------------
    // Drag zone overlay (highlight on hover)
    // -------------------------------
    public class DragZoneOverlay : Form
    {
        protected override bool ShowWithoutActivation => true;

        private readonly string zoneNumber;
        private readonly int zoneIndex;
        private bool isHighlighted = false;

        private static readonly Color[] normalColors = new Color[]
        {
            Color.LightBlue, Color.LightCoral, Color.LightGreen, Color.Orange,
            Color.Plum, Color.Khaki, Color.Orchid, Color.LightCyan, Color.Pink
        };

        private static readonly Color[] highlightColors = new Color[]
        {
            Color.Blue, Color.Red, Color.Green, Color.DarkOrange, Color.Purple,
            Color.Gold, Color.Magenta, Color.Cyan, Color.HotPink
        };

        public DragZoneOverlay(Rectangle bounds, string number, int index)
        {
            zoneNumber = number;
            zoneIndex = index;

            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = bounds;

            int colorIndex = (int.Parse(number) - 1) % normalColors.Length;
            this.BackColor = normalColors[colorIndex];
            this.Opacity = 0.6;

            this.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        }

        public void SetHighlighted(bool highlighted)
        {
            if (isHighlighted != highlighted)
            {
                isHighlighted = highlighted;

                int colorIndex = (int.Parse(zoneNumber) - 1) % normalColors.Length;
                this.BackColor = highlighted ? highlightColors[colorIndex] : normalColors[colorIndex];
                this.Opacity = highlighted ? 0.8 : 0.6;
                this.Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            int borderWidth = isHighlighted ? 6 : 3;
            using (var borderPen = new Pen(Color.White, borderWidth))
            {
                int offset = borderWidth / 2;
                e.Graphics.DrawRectangle(borderPen, offset, offset, this.Width - borderWidth, this.Height - borderWidth);
            }

            using (var backgroundBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            using (var textBrush = new SolidBrush(Color.White))
            using (var font = new Font("Arial", isHighlighted ? 36 : 28, FontStyle.Bold))
            {
                var size = e.Graphics.MeasureString(zoneNumber, font);
                var point = new PointF((this.Width - size.Width) / 2, (this.Height - size.Height) / 2);

                var textRect = new RectangleF(point.X - 10, point.Y - 5, size.Width + 20, size.Height + 10);
                e.Graphics.FillRectangle(backgroundBrush, textRect);
                e.Graphics.DrawString(zoneNumber, font, textBrush, point);
            }

            if (isHighlighted)
            {
                using (var textBrush = new SolidBrush(Color.White))
                using (var font = new Font("Arial", 12, FontStyle.Bold))
                {
                    string instruction = "Release to snap window here";
                    var size = e.Graphics.MeasureString(instruction, font);
                    var point = new PointF((this.Width - size.Width) / 2, this.Height - 30);

                    using (var backgroundBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                    {
                        var textRect = new RectangleF(point.X - 5, point.Y - 2, size.Width + 10, size.Height + 4);
                        e.Graphics.FillRectangle(backgroundBrush, textRect);
                    }

                    e.Graphics.DrawString(instruction, font, textBrush, point);
                }
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeConstants.WS_EX_LAYERED;
                cp.ExStyle |= NativeConstants.WS_EX_TRANSPARENT;
                cp.ExStyle |= NativeConstants.WS_EX_NOACTIVATE;
                return cp;
            }
        }
    }

    // -------------------------------
    // Global mouse hook for drag detection
    // -------------------------------
    public class GlobalMouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;

        private LowLevelMouseProc _proc = HookCallback;
        private IntPtr _hookID = IntPtr.Zero;
        private static GlobalMouseHook _instance;

        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        public event EventHandler<MouseEventArgs> MouseDown;
        public event EventHandler<MouseEventArgs> MouseUp;
        public event EventHandler<MouseEventArgs> MouseMove;

        public GlobalMouseHook()
        {
            _instance = this;
            _hookID = SetHook(_proc);
        }

        private static IntPtr SetHook(LowLevelMouseProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                if (wParam == (IntPtr)WM_LBUTTONDOWN)
                {
                    _instance?.MouseDown?.Invoke(_instance, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                }
                else if (wParam == (IntPtr)WM_LBUTTONUP)
                {
                    _instance?.MouseUp?.Invoke(_instance, new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
                }
                else if (wParam == (IntPtr)WM_MOUSEMOVE)
                {
                    _instance?.MouseMove?.Invoke(_instance, new MouseEventArgs(MouseButtons.None, 0, 0, 0, 0));
                }
            }

            return CallNextHookEx(_instance?._hookID ?? IntPtr.Zero, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
                _hookID = IntPtr.Zero;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook,
            LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }

    // -------------------------------
    // Global keyboard hook for hotkeys
    // -------------------------------
    public class GlobalKeyboardHook : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private Window window = new Window();
        private int currentId = 0;

        public GlobalKeyboardHook()
        {
            window.KeyPressed += delegate (object sender, KeyPressedEventArgs args)
            {
                KeyDown?.Invoke(this, args);
            };
        }

        public event EventHandler<KeyPressedEventArgs> KeyDown;

        public void RegisterHotKey(HotKeyModifiers modifier, Keys key)
        {
            currentId++;
            if (!RegisterHotKey(window.Handle, currentId, (int)modifier, (int)key))
                throw new InvalidOperationException("Couldn't register the hot key.");
        }

        public void Dispose()
        {
            for (int i = currentId; i > 0; i--)
            {
                UnregisterHotKey(window.Handle, i);
            }
            window.Dispose();
        }

        private class Window : NativeWindow, IDisposable
        {
            private static int WM_HOTKEY = 0x0312;

            public Window()
            {
                this.CreateHandle(new CreateParams());
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);

                if (m.Msg == WM_HOTKEY)
                {
                    Keys key = (Keys)(((int)m.LParam >> 16) & 0xFFFF);
                    HotKeyModifiers modifier = (HotKeyModifiers)((int)m.LParam & 0xFFFF);

                    KeyPressed?.Invoke(this, new KeyPressedEventArgs() { Modifier = modifier, Key = key });
                }
            }

            public event EventHandler<KeyPressedEventArgs> KeyPressed;

            public void Dispose()
            {
                this.DestroyHandle();
            }
        }
    }

    public class KeyPressedEventArgs : EventArgs
    {
        public HotKeyModifiers Modifier { get; set; }
        public Keys Key { get; set; }
    }

    [Flags]
    public enum HotKeyModifiers : uint
    {
        Alt = 1,
        Control = 2,
        Shift = 4,
        Windows = 8
    }
}
