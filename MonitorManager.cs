using System;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace LightweightZoneManager
{
    /// <summary>
    /// Manages monitor detection and change tracking
    /// </summary>
    public class MonitorManager
    {
        /// <summary>
        /// Generate a fingerprint of the current monitor configuration
        /// Format: "Count:Width1xHeight1@X1,Y1;Width2xHeight2@X2,Y2;..."
        /// </summary>
        public static string GetMonitorFingerprint()
        {
            Screen[] monitors = Screen.AllScreens;
            var sb = new StringBuilder();

            sb.Append($"{monitors.Length}:");

            for (int i = 0; i < monitors.Length; i++)
            {
                if (i > 0) sb.Append(";");

                var monitor = monitors[i];
                sb.Append($"{monitor.Bounds.Width}x{monitor.Bounds.Height}@{monitor.Bounds.X},{monitor.Bounds.Y}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Compare two monitor fingerprints to detect changes
        /// </summary>
        public static bool HasMonitorConfigChanged(string oldFingerprint, string newFingerprint)
        {
            if (string.IsNullOrEmpty(oldFingerprint))
                return false; // First run, no change

            return !string.Equals(oldFingerprint, newFingerprint, StringComparison.Ordinal);
        }

        /// <summary>
        /// Get current monitor count
        /// </summary>
        public static int GetMonitorCount()
        {
            return Screen.AllScreens.Length;
        }

        /// <summary>
        /// Get detailed information about all monitors
        /// </summary>
        public static string GetMonitorInfo()
        {
            Screen[] monitors = Screen.AllScreens;
            var sb = new StringBuilder();

            sb.AppendLine($"Detected {monitors.Length} monitor(s):");
            sb.AppendLine();

            for (int i = 0; i < monitors.Length; i++)
            {
                var monitor = monitors[i];
                sb.AppendLine($"Monitor {i + 1}:");
                sb.AppendLine($"  Resolution: {monitor.Bounds.Width} x {monitor.Bounds.Height}");
                sb.AppendLine($"  Position: ({monitor.Bounds.X}, {monitor.Bounds.Y})");
                sb.AppendLine($"  Working Area: {monitor.WorkingArea.Width} x {monitor.WorkingArea.Height}");
                sb.AppendLine($"  Primary: {(monitor.Primary ? "Yes" : "No")}");
                sb.AppendLine($"  Device: {monitor.DeviceName}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Analyze monitor change and provide detailed description
        /// </summary>
        public static string DescribeMonitorChange(string oldFingerprint, string newFingerprint)
        {
            if (string.IsNullOrEmpty(oldFingerprint))
                return "Initial monitor configuration detected.";

            // Parse fingerprints
            int oldCount = ParseMonitorCount(oldFingerprint);
            int newCount = ParseMonitorCount(newFingerprint);

            var sb = new StringBuilder();
            sb.AppendLine("Monitor configuration has changed:");
            sb.AppendLine();

            if (oldCount != newCount)
            {
                sb.AppendLine($"  Monitor count: {oldCount} → {newCount}");

                if (newCount > oldCount)
                    sb.AppendLine($"  ({newCount - oldCount} monitor(s) added)");
                else
                    sb.AppendLine($"  ({oldCount - newCount} monitor(s) removed)");
            }
            else
            {
                sb.AppendLine($"  Monitor count unchanged ({newCount} monitors)");
                sb.AppendLine("  But resolution or arrangement has changed");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Parse monitor count from fingerprint
        /// </summary>
        private static int ParseMonitorCount(string fingerprint)
        {
            if (string.IsNullOrEmpty(fingerprint))
                return 0;

            int colonIndex = fingerprint.IndexOf(':');
            if (colonIndex > 0 && int.TryParse(fingerprint.Substring(0, colonIndex), out int count))
                return count;

            return 0;
        }

        /// <summary>
        /// Check if a specific monitor number is currently available
        /// </summary>
        public static bool IsMonitorAvailable(int monitorNumber)
        {
            int monitorCount = Screen.AllScreens.Length;
            return monitorNumber >= 1 && monitorNumber <= monitorCount;
        }

        /// <summary>
        /// Get all monitor numbers referenced in zone configs
        /// </summary>
        public static int[] GetReferencedMonitors(System.Collections.Generic.List<ZoneConfig> zoneConfigs)
        {
            return zoneConfigs
                .Select(z => z.Monitor)
                .Distinct()
                .OrderBy(m => m)
                .ToArray();
        }

        /// <summary>
        /// Check if any zones reference monitors that no longer exist
        /// </summary>
        public static bool HasMissingMonitors(System.Collections.Generic.List<ZoneConfig> zoneConfigs)
        {
            int currentMonitorCount = GetMonitorCount();
            return zoneConfigs.Any(z => z.Monitor < 1 || z.Monitor > currentMonitorCount);
        }

        /// <summary>
        /// Get count of zones that reference missing monitors
        /// </summary>
        public static int CountZonesOnMissingMonitors(System.Collections.Generic.List<ZoneConfig> zoneConfigs)
        {
            int currentMonitorCount = GetMonitorCount();
            return zoneConfigs.Count(z => z.Monitor < 1 || z.Monitor > currentMonitorCount);
        }
    }
}
