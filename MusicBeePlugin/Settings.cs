using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace MusicBeePlugin
{
    public class ProviderSetting
    {
        public string Name { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public class PluginSettings
    {
        public List<ProviderSetting> Providers { get; set; } = new List<ProviderSetting>();
        public int TitleMatchThresholdPercent { get; set; } = 85;
        public int ArtistMatchThresholdPercent { get; set; } = 85;
        public bool EnableSyncedLyrics { get; set; } = true;

        public bool IsEnabled(string providerName)
        {
            var entry = Providers.FirstOrDefault(p => string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase));
            return entry == null || entry.Enabled;
        }

        public void EnsureDefaults(IEnumerable<string> providerNames)
        {
            foreach (var name in providerNames)
            {
                if (!Providers.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    Providers.Add(new ProviderSetting { Name = name, Enabled = true });
                }
            }
        }

        public static PluginSettings Load(string directory)
        {
            try
            {
                var file = GetFilePath(directory);
                if (!File.Exists(file))
                {
                    return new PluginSettings();
                }

                using (var stream = File.OpenRead(file))
                {
                    return (PluginSettings)new XmlSerializer(typeof(PluginSettings)).Deserialize(stream);
                }
            }
            catch (Exception)
            {
                return new PluginSettings();
            }
        }

        public void Save(string directory)
        {
            try
            {
                var filePath = GetFilePath(directory);
                var dirPath = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }

                using (var stream = File.Create(filePath))
                {
                    new XmlSerializer(typeof(PluginSettings)).Serialize(stream, this);
                }
            }
            catch (Exception)
            {
            }
        }

        private static string GetFilePath(string directory)
        {
            return Path.Combine(directory, "AutoLyrics", "settings.xml");
        }
    }
}
