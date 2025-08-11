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
        private List<Rectangle> zones = new List<Rectangle>();
        private List<Form> zoneOverlays = new List<Form>();
        private GlobalKeyboardHook keyboardHook;
        private GlobalMouseHook mouseHook;
        private string configPath;
        private bool editMode = false;
        private List<ZoneConfig> zoneConfigs = new List<ZoneConfig>();
        private bool isDragSnapping = false;
        private IntPtr draggedWindow = IntPtr.Zero;

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

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int VK_CONTROL = 0x11;
        private const int VK_LBUTTON = 0x01;

        public ZoneManager()
        {
            InitializeComponent();

            // Set up config file path in the same directory as the executable
            configPath = Path.Combine(Application.StartupPath, "ZoneConfig.xml");

            SetupTrayIcon();
            LoadZoneConfig(); // Load saved zones or create defaults
            SetupGlobalHotkey();
            SetupMouseHook(); // Add drag-and-drop functionality
        }

        private void InitializeComponent()
        {
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            this.Visible = false;
        }

        private void SetupTrayIcon()
        {
            // Create a custom icon for the tray
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
            trayMenu.Items.Add("Save Current Layout", null, SaveZones_Click);
            trayMenu.Items.Add("Reset to Defaults", null, ResetZones_Click);
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Monitor Info", null, ShowMonitorInfo_Click);
            trayMenu.Items.Add("Restart as Admin (for hotkeys)", null, RestartAsAdmin_Click);
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Usage Instructions", null, PinInstructions_Click);
            trayMenu.Items.Add("-");
            trayMenu.Items.Add("Exit", null, Exit_Click);

            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += TrayIcon_DoubleClick;
        }

        private Icon CreateCustomIcon()
        {
            // Create a 16x16 bitmap for the tray icon
            Bitmap bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                // Clear background
                g.Clear(Color.Transparent);

                // Draw a grid pattern to represent zones
                using (Pen pen = new Pen(Color.White, 1))
                {
                    // Draw a 3x3 grid (window zones)
                    // Vertical lines
                    g.DrawLine(pen, 5, 2, 5, 13);
                    g.DrawLine(pen, 10, 2, 10, 13);
                    // Horizontal lines  
                    g.DrawLine(pen, 2, 5, 13, 5);
                    g.DrawLine(pen, 2, 10, 13, 10);

                    // Draw border
                    g.DrawRectangle(pen, 2, 2, 11, 11);
                }

                // Add a small accent color
                using (Brush brush = new SolidBrush(Color.LightBlue))
                {
                    g.FillRectangle(brush, 3, 3, 2, 2); // Top-left zone highlighted
                }
            }

            // Convert bitmap to icon
            IntPtr hIcon = bitmap.GetHicon();
            Icon icon = Icon.FromHandle(hIcon);

            return icon;
        }

        private void LoadZoneConfig()
        {
            try
            {
                if (File.Exists(configPath))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(ZoneSettings));
                    using (FileStream stream = new FileStream(configPath, FileMode.Open))
                    {
                        var settings = (ZoneSettings)serializer.Deserialize(stream);
                        zoneConfigs = settings.Zones;
                        BuildZonesFromConfig();
                    }
                }
                else
                {
                    CreateDefaultZones();
                    SaveZoneConfig();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading zone config: {ex.Message}\nUsing default zones.", "Config Error");
                CreateDefaultZones();
            }
        }

        private void SaveZoneConfig()
        {
            try
            {
                var settings = new ZoneSettings { Zones = zoneConfigs };
                XmlSerializer serializer = new XmlSerializer(typeof(ZoneSettings));
                using (FileStream stream = new FileStream(configPath, FileMode.Create))
                {
                    serializer.Serialize(stream, settings);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving zone config: {ex.Message}", "Save Error");
            }
        }

        private void CreateDefaultZones()
        {
            zoneConfigs.Clear();

            // Get all monitors
            Screen[] monitors = Screen.AllScreens;

            // MONITOR 1 ZONES (Usually your main monitor)
            if (monitors.Length >= 1)
            {
                // 2x2 Grid on Monitor 1
                zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 0, Y = 0, Width = 50, Height = 50, Name = "Top-Left" });
                zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 50, Y = 0, Width = 50, Height = 50, Name = "Top-Right" });
                zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 0, Y = 50, Width = 50, Height = 50, Name = "Bottom-Left" });
                zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 50, Y = 50, Width = 50, Height = 50, Name = "Bottom-Right" });

                // Full halves on Monitor 1
                zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 0, Y = 0, Width = 50, Height = 100, Name = "Left Half" });
                zoneConfigs.Add(new ZoneConfig { Monitor = 1, X = 50, Y = 0, Width = 50, Height = 100, Name = "Right Half" });
            }

            // MONITOR 2 ZONES (Secondary monitor)
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
                if (config.Monitor < 1 || config.Monitor > monitors.Length)
                    continue;

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

            // Try to register hotkeys - gracefully handle failure
            try
            {
                // Try Ctrl+Shift+` first
                keyboardHook.RegisterHotKey(HotKeyModifiers.Control | HotKeyModifiers.Shift, Keys.Oemtilde);
                for (int i = 1; i <= 9; i++)
                {
                    keyboardHook.RegisterHotKey(HotKeyModifiers.Control | HotKeyModifiers.Shift, Keys.D0 + i);
                }

                // Show success message in tray
                trayIcon.ShowBalloonTip(3000, "Zone Manager",
                    "✅ Hotkeys: Ctrl+Shift+` (zones), Ctrl+Shift+1-9 (snap)\n" +
                    "✅ Drag & Drop: Hold Ctrl while dragging windows", ToolTipIcon.Info);
            }
            catch (Exception)
            {
                // Don't crash - just continue without hotkeys
                trayIcon.ShowBalloonTip(4000, "Zone Manager",
                    "✅ Drag & Drop: Hold Ctrl while dragging windows\n" +
                    "⚠️ Hotkeys unavailable (run as admin for hotkeys)", ToolTipIcon.Info);

                // Update tray tooltip to reflect features available
                trayIcon.Text = "Zone Manager - Hold Ctrl while dragging windows to snap";
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
            catch (Exception)
            {
                // Mouse hook is optional - continue without it
            }
        }

        private void MouseHook_MouseDown(object sender, MouseEventArgs e)
        {
            // Check if Ctrl is held and left mouse button is pressed
            if (e.Button == MouseButtons.Left && IsCtrlPressed())
            {
                POINT cursorPos;
                GetCursorPos(out cursorPos);
                draggedWindow = WindowFromPoint(cursorPos);

                // Only start drag snapping for visible, non-minimized windows
                if (draggedWindow != IntPtr.Zero && IsWindowVisible(draggedWindow) && !IsIconic(draggedWindow))
                {
                    // Don't snap our own overlays or the taskbar
                    string className = GetWindowClassName(draggedWindow);
                    if (!className.Contains("ZoneOverlay") && !className.Contains("Shell_TrayWnd"))
                    {
                        isDragSnapping = true;
                        ShowDragZones();
                    }
                }
            }
        }

        private void MouseHook_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragSnapping)
            {
                // Update zone highlighting based on mouse position
                HighlightZoneUnderMouse();
            }
        }

        private void MouseHook_MouseUp(object sender, MouseEventArgs e)
        {
            if (isDragSnapping && e.Button == MouseButtons.Left)
            {
                // Find which zone the mouse is over and snap window to it
                int zoneIndex = GetZoneUnderMouse();
                if (zoneIndex >= 0 && draggedWindow != IntPtr.Zero)
                {
                    SnapWindowToZone(draggedWindow, zoneIndex);
                }

                EndDragSnapping();
            }
        }

        private bool IsCtrlPressed()
        {
            return (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
        }

        private string GetWindowClassName(IntPtr hWnd)
        {
            var className = new System.Text.StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);
            return className.ToString();
        }

        private void ShowDragZones()
        {
            HideZones(); // Clear existing zones

            for (int i = 0; i < zones.Count && i < 9; i++)
            {
                var overlay = new DragZoneOverlay(zones[i], (i + 1).ToString(), i);
                overlay.Show();
                zoneOverlays.Add(overlay);
            }
            zonesVisible = true;
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

        private void SnapWindowToZone(IntPtr window, int zoneIndex)
        {
            if (zoneIndex >= 0 && zoneIndex < zones.Count)
            {
                Rectangle zone = zones[zoneIndex];
                SetWindowPos(window, IntPtr.Zero,
                    zone.X, zone.Y, zone.Width, zone.Height,
                    SWP_NOZORDER | SWP_SHOWWINDOW);
            }
        }

        private void EndDragSnapping()
        {
            isDragSnapping = false;
            draggedWindow = IntPtr.Zero;
            HideZones();
        }

        private void KeyboardHook_KeyDown(object sender, KeyPressedEventArgs e)
        {
            // Ctrl+Shift+` to toggle zones
            if (e.Modifier == (HotKeyModifiers.Control | HotKeyModifiers.Shift) && e.Key == Keys.Oemtilde)
            {
                ToggleZones();
            }
            // Ctrl+Shift+1-9 to snap to zone
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
            if (zonesVisible)
                HideZones();
            else
                ShowZones();
        }

        private void ShowZones()
        {
            if (editMode)
            {
                ShowEditableZones();
                return;
            }

            HideZones(); // Clear existing overlays first

            for (int i = 0; i < zones.Count && i < 9; i++) // Limit to 9 zones for number keys
            {
                var overlay = new ZoneOverlay(zones[i], (i + 1).ToString());
                overlay.Show();
                zoneOverlays.Add(overlay);
            }
            zonesVisible = true;
        }

        private void HideZones()
        {
            foreach (var overlay in zoneOverlays)
            {
                overlay.Close();
            }
            zoneOverlays.Clear();
            zonesVisible = false;
            editMode = false;
        }

        private void SnapActiveWindowToZone(int zoneIndex)
        {
            IntPtr activeWindow = GetForegroundWindow();
            if (activeWindow != IntPtr.Zero && IsWindowVisible(activeWindow) && !IsIconic(activeWindow))
            {
                Rectangle zone = zones[zoneIndex];
                SetWindowPos(activeWindow, IntPtr.Zero,
                    zone.X, zone.Y, zone.Width, zone.Height,
                    SWP_NOZORDER | SWP_SHOWWINDOW);
            }
            HideZones();
        }

        private void TrayIcon_DoubleClick(object sender, EventArgs e)
        {
            ToggleZones();
        }

        private void ShowZones_Click(object sender, EventArgs e)
        {
            ShowZones();
        }

        private void HideZones_Click(object sender, EventArgs e)
        {
            HideZones();
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

        private void ShowEditableZones()
        {
            HideZones(); // Clear existing overlays first

            for (int i = 0; i < zones.Count && i < 9; i++)
            {
                var overlay = new EditableZoneOverlay(zones[i], (i + 1).ToString(), i, this);
                overlay.Show();
                zoneOverlays.Add(overlay);
            }
            zonesVisible = true;

            // Show instructions
            trayIcon.ShowBalloonTip(5000, "Zone Editor",
                "Drag zones to move, drag corners to resize. Right-click tray → 'Save Current Layout' when done.",
                ToolTipIcon.Info);
        }

        public void UpdateZoneFromEdit(int zoneIndex, Rectangle newBounds)
        {
            if (zoneIndex >= 0 && zoneIndex < zones.Count && zoneIndex < zoneConfigs.Count)
            {
                zones[zoneIndex] = newBounds;

                // Update config with percentages
                Screen[] monitors = Screen.AllScreens;
                var config = zoneConfigs[zoneIndex];

                if (config.Monitor >= 1 && config.Monitor <= monitors.Length)
                {
                    Rectangle screen = monitors[config.Monitor - 1].WorkingArea;

                    config.X = ((double)(newBounds.X - screen.X) / screen.Width) * 100;
                    config.Y = ((double)(newBounds.Y - screen.Y) / screen.Height) * 100;
                    config.Width = ((double)newBounds.Width / screen.Width) * 100;
                    config.Height = ((double)newBounds.Height / screen.Height) * 100;
                }
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
                startInfo.Verb = "runas"; // This requests admin privileges
                Process.Start(startInfo);

                // Close current instance
                Exit_Click(sender, e);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not restart as administrator: {ex.Message}", "Restart Failed");
            }
        }

        private void PinInstructions_Click(object sender, EventArgs e)
        {
            string instructions = "🎯 HOW TO USE ZONE MANAGER:\n\n" +
                                "DRAG & DROP SNAPPING (No Admin Required!):\n" +
                                "• Hold CTRL while dragging any window\n" +
                                "• Zones appear automatically\n" +
                                "• Drag over a zone to highlight it\n" +
                                "• Release mouse to snap window to zone\n\n" +
                                "HOTKEYS (Admin Mode Only):\n" +
                                "• Ctrl+Shift+` = Show/hide zones\n" +
                                "• Ctrl+Shift+1-9 = Snap active window to zone\n\n" +
                                "📌 HOW TO PIN ICON TO SYSTEM TRAY:\n\n" +
                                "Method 1 - Windows 10/11:\n" +
                                "1. Right-click empty space on taskbar\n" +
                                "2. Select 'Taskbar settings'\n" +
                                "3. Scroll to 'Notification area'\n" +
                                "4. Click 'Turn system icons on or off'\n" +
                                "5. Find 'LightweightZoneManager' and turn it ON\n\n" +
                                "Method 2 - Show All Icons:\n" +
                                "1. In Taskbar settings → Notification area\n" +
                                "2. Click 'Select which icons appear on taskbar'\n" +
                                "3. Turn ON: 'Always show all icons in notification area'\n\n" +
                                "Method 3 - Quick Access:\n" +
                                "1. Look for the up arrow (^) next to system tray\n" +
                                "2. Click it to see hidden icons\n" +
                                "3. Drag the Zone Manager icon to the main tray area";

            MessageBox.Show(instructions, "Zone Manager Usage & Pin Instructions", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }
    }

    // Zone overlay form
    public class ZoneOverlay : Form
    {
        private string zoneNumber;
        private static Color[] zoneColors = new Color[]
        {
            Color.Blue,         // Blue
            Color.Red,          // Red  
            Color.Green,        // Green
            Color.Orange,       // Orange
            Color.Purple,       // Purple
            Color.Yellow,       // Yellow
            Color.Magenta,      // Magenta
            Color.Cyan,         // Cyan
            Color.Pink          // Pink
        };

        public ZoneOverlay(Rectangle bounds, string number)
        {
            zoneNumber = number;

            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = bounds;

            // Use different colors for each zone
            int colorIndex = (int.Parse(number) - 1) % zoneColors.Length;
            this.BackColor = zoneColors[colorIndex];
            this.Opacity = 0.7; // Use form opacity instead of color alpha

            // Auto-hide after 8 seconds (longer duration)
            var timer = new Timer();
            timer.Interval = 8000;
            timer.Tick += (s, e) => { this.Close(); timer.Dispose(); };
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Draw thick border for better distinction
            using (var borderPen = new Pen(Color.White, 4))
            {
                e.Graphics.DrawRectangle(borderPen, 2, 2, this.Width - 4, this.Height - 4);
            }

            // Draw zone number with background for better readability
            using (var backgroundBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            using (var textBrush = new SolidBrush(Color.White))
            using (var font = new Font("Arial", 32, FontStyle.Bold))
            {
                var size = e.Graphics.MeasureString(zoneNumber, font);
                var point = new PointF(
                    (this.Width - size.Width) / 2,
                    (this.Height - size.Height) / 2
                );

                // Draw background rectangle for text
                var textRect = new RectangleF(point.X - 10, point.Y - 5, size.Width + 20, size.Height + 10);
                e.Graphics.FillRectangle(backgroundBrush, textRect);

                // Draw the number
                e.Graphics.DrawString(zoneNumber, font, textBrush, point);
            }

            // Draw zone info at top-left corner
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
                cp.ExStyle |= 0x80000; // WS_EX_LAYERED
                cp.ExStyle |= 0x20; // WS_EX_TRANSPARENT
                return cp;
            }
        }
    }

    // Editable zone overlay with drag and resize functionality  
    public class EditableZoneOverlay : Form
    {
        private string zoneNumber;
        private int zoneIndex;
        private ZoneManager parentManager;
        private bool isDragging = false;
        private bool isResizing = false;
        private Point dragOffset;
        private ResizeDirection resizeDirection;

        private enum ResizeDirection
        {
            None, TopLeft, TopRight, BottomLeft, BottomRight,
            Left, Right, Top, Bottom, Move
        }

        private static Color[] zoneColors = new Color[]
        {
            Color.Blue,         // Blue
            Color.Red,          // Red  
            Color.Green,        // Green
            Color.Orange,       // Orange
            Color.Purple,       // Purple
            Color.Yellow,       // Yellow
            Color.Magenta,      // Magenta
            Color.Cyan,         // Cyan
            Color.Pink          // Pink
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

            // Use different colors for each zone
            int colorIndex = (int.Parse(number) - 1) % zoneColors.Length;
            this.BackColor = zoneColors[colorIndex];
            this.Opacity = 0.8; // Slightly more opaque for editing

            // Enable mouse events
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
                // Update cursor based on position
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

            // Minimum size constraints
            if (newBounds.Width >= 50 && newBounds.Height >= 30)
            {
                this.Bounds = newBounds;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // Draw thick border for better distinction
            using (var borderPen = new Pen(Color.White, 3))
            {
                e.Graphics.DrawRectangle(borderPen, 1, 1, this.Width - 2, this.Height - 2);
            }

            // Draw resize handles
            const int handleSize = 8;
            using (var handleBrush = new SolidBrush(Color.White))
            {
                // Corner handles
                e.Graphics.FillRectangle(handleBrush, 0, 0, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, this.Width - handleSize, 0, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, 0, this.Height - handleSize, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, this.Width - handleSize, this.Height - handleSize, handleSize, handleSize);

                // Edge handles
                e.Graphics.FillRectangle(handleBrush, this.Width / 2 - handleSize / 2, 0, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, this.Width / 2 - handleSize / 2, this.Height - handleSize, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, 0, this.Height / 2 - handleSize / 2, handleSize, handleSize);
                e.Graphics.FillRectangle(handleBrush, this.Width - handleSize, this.Height / 2 - handleSize / 2, handleSize, handleSize);
            }

            // Draw zone number and info
            using (var backgroundBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            using (var textBrush = new SolidBrush(Color.White))
            using (var font = new Font("Arial", 24, FontStyle.Bold))
            using (var smallFont = new Font("Arial", 9, FontStyle.Bold))
            {
                var size = e.Graphics.MeasureString(zoneNumber, font);
                var point = new PointF(
                    (this.Width - size.Width) / 2,
                    (this.Height - size.Height) / 2
                );

                // Draw background rectangle for text
                var textRect = new RectangleF(point.X - 10, point.Y - 5, size.Width + 20, size.Height + 10);
                e.Graphics.FillRectangle(backgroundBrush, textRect);

                // Draw the number
                e.Graphics.DrawString(zoneNumber, font, textBrush, point);

                // Draw instructions at the bottom
                string instructions = "Drag to move • Drag corners/edges to resize";
                e.Graphics.DrawString(instructions, smallFont, textBrush, new PointF(10, this.Height - 25));
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000; // WS_EX_LAYERED
                return cp;
            }
        }
    }

    // Drag zone overlay for drag-and-drop functionality
    public class DragZoneOverlay : Form
    {
        private string zoneNumber;
        private int zoneIndex;
        private bool isHighlighted = false;

        private static Color[] normalColors = new Color[]
        {
            Color.LightBlue, Color.LightCoral, Color.LightGreen, Color.Orange,
            Color.Plum, Color.Khaki, Color.Orchid, Color.LightCyan, Color.Pink
        };

        private static Color[] highlightColors = new Color[]
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

            // Set up for transparency
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

            // Draw border - thicker when highlighted
            int borderWidth = isHighlighted ? 6 : 3;
            using (var borderPen = new Pen(Color.White, borderWidth))
            {
                int offset = borderWidth / 2;
                e.Graphics.DrawRectangle(borderPen, offset, offset,
                    this.Width - borderWidth, this.Height - borderWidth);
            }

            // Draw zone number
            using (var backgroundBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
            using (var textBrush = new SolidBrush(Color.White))
            using (var font = new Font("Arial", isHighlighted ? 36 : 28, FontStyle.Bold))
            {
                var size = e.Graphics.MeasureString(zoneNumber, font);
                var point = new PointF(
                    (this.Width - size.Width) / 2,
                    (this.Height - size.Height) / 2
                );

                var textRect = new RectangleF(point.X - 10, point.Y - 5, size.Width + 20, size.Height + 10);
                e.Graphics.FillRectangle(backgroundBrush, textRect);
                e.Graphics.DrawString(zoneNumber, font, textBrush, point);
            }

            // Show drop instruction when highlighted
            if (isHighlighted)
            {
                using (var textBrush = new SolidBrush(Color.White))
                using (var font = new Font("Arial", 12, FontStyle.Bold))
                {
                    string instruction = "Release to snap window here";
                    var size = e.Graphics.MeasureString(instruction, font);
                    var point = new PointF(
                        (this.Width - size.Width) / 2,
                        this.Height - 30
                    );

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
                cp.ExStyle |= 0x80000; // WS_EX_LAYERED
                cp.ExStyle |= 0x20; // WS_EX_TRANSPARENT
                return cp;
            }
        }
    }

    // Global mouse hook for drag detection
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
                return SetWindowsHookEx(WH_MOUSE_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
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

    // Global keyboard hook class
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