using System;
using System.Linq;
using MusicBeePlugin.Providers;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        internal static readonly ILyricsProvider[] LyricProviders = ProviderRegistry.CreateAllProviders();
        private static readonly string[] ProviderNames = ProviderRegistry.GetAllProviderNames();

        internal static PluginSettings Settings = new PluginSettings();

        private System.Windows.Forms.CheckBox[] providerCheckBoxes;
        private System.Windows.Forms.NumericUpDown titleThresholdUpDown;
        private System.Windows.Forms.NumericUpDown artistThresholdUpDown;
        private System.Windows.Forms.CheckBox syncedLyricsCheckBox;

        public string[] GetProviders()
        {
            return LyricProviders
                .Where(p => Settings.IsEnabled(p.Name))
                .Select(p => p.Name)
                .ToArray();
        }

        public string RetrieveLyrics(string sourceFileUrl, string artist, string trackTitle, string album, bool synchronisedPreferred, string provider)
        {
            var target = LyricProviders.FirstOrDefault(p => string.Equals(p.Name, provider, StringComparison.OrdinalIgnoreCase));
            if (target == null || !Settings.IsEnabled(provider))
            {
                return null;
            }

            try
            {
                double? duration = null;
                if (!string.IsNullOrEmpty(sourceFileUrl))
                {
                    try
                    {
                        var raw = mbApiInterface.Library_GetFileProperty(sourceFileUrl, FilePropertyType.Duration);
                        if (double.TryParse(raw, out var seconds) && seconds > 0)
                        {
                            duration = seconds;
                        }
                    }
                    catch (Exception)
                    {
                        // 曲長が取得できない場合は duration null で続行
                    }
                }

                var preferSync = Settings.EnableSyncedLyrics && synchronisedPreferred;

                if (target is ISyncedLyricsAwareProvider syncAware)
                {
                    return syncAware.FetchLyrics(artist, trackTitle, album, duration, preferSync);
                }

                if (target is IDurationAwareProvider durationAware)
                {
                    return durationAware.FetchLyrics(artist, trackTitle, album, duration);
                }

                return target.FetchLyrics(artist, trackTitle, album);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
