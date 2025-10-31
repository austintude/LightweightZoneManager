using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace LightweightZoneManager
{
    /// <summary>
    /// Represents a single zone configuration with position stored as percentages
    /// </summary>
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

    /// <summary>
    /// Container for all zone configurations with versioning and monitor fingerprint
    /// </summary>
    [Serializable]
    public class ZoneSettings
    {
        public List<ZoneConfig> Zones { get; set; } = new List<ZoneConfig>();
        public int Version { get; set; } = 1;

        /// <summary>
        /// Monitor configuration fingerprint to detect hardware changes
        /// Format: "MonitorCount:Width1x Height1,Width2xHeight2,..."
        /// </summary>
        public string MonitorFingerprint { get; set; }
    }

    /// <summary>
    /// Handles loading, saving, and validating zone configurations
    /// </summary>
    public class ZoneConfigurationManager
    {
        private readonly string configPath;

        public ZoneConfigurationManager(string configFilePath)
        {
            configPath = configFilePath;
        }

        /// <summary>
        /// Load zone configuration from XML file
        /// </summary>
        public ZoneSettings LoadConfig()
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
                        Console.WriteLine("Config file appears to be corrupted (too short)");
                        throw new InvalidDataException("Config file is too short to be valid");
                    }

                    XmlSerializer serializer = new XmlSerializer(typeof(ZoneSettings));
                    using (StringReader reader = new StringReader(xmlContent))
                    {
                        var settings = (ZoneSettings)serializer.Deserialize(reader);

                        if (settings.Zones == null)
                            settings.Zones = new List<ZoneConfig>();

                        Console.WriteLine($"Loaded {settings.Zones.Count} zones from config");
                        return settings;
                    }
                }
                else
                {
                    Console.WriteLine("Config file not found");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading zone config: {ex.Message}");

                // Backup the corrupted file
                BackupCorruptedConfig();

                throw;
            }
        }

        /// <summary>
        /// Save zone configuration to XML file
        /// </summary>
        public void SaveConfig(ZoneSettings settings)
        {
            try
            {
                Console.WriteLine($"Saving {settings.Zones.Count} zones to: {configPath}");

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
                throw;
            }
        }

        /// <summary>
        /// Create a timestamped backup of the current config file
        /// </summary>
        private void BackupCorruptedConfig()
        {
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
        }
    }
}
