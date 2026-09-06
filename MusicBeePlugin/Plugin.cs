using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MusicBeePlugin
{
    public partial class Plugin
    {
        private MusicBeeApiInterface mbApiInterface;
        private PluginInfo about = new PluginInfo();
        private string settingsDirectory;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            mbApiInterface = new MusicBeeApiInterface();
            mbApiInterface.Initialise(apiInterfacePtr);

            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
            }
            catch { }

            settingsDirectory = mbApiInterface.Setting_GetPersistentStoragePath();
            Settings = PluginSettings.Load(settingsDirectory);
            Settings.EnsureDefaults(ProviderNames);
            Providers.OriconProvider.SetCacheDirectory(System.IO.Path.Combine(settingsDirectory, "AutoLyrics"));

            about.PluginInfoVersion = PluginInfoVersion;
            about.Name = "AutoLyrics";
            about.Description = "Retrieves plain and synced lyrics from multiple sources (LRCLIB, Genius, Japanese sites).";
            about.Author = "treeturtlefall-arch";
            about.TargetApplication = "";
            about.Type = PluginType.LyricsRetrieval;
            about.VersionMajor = 0;
            about.VersionMinor = 4;
            about.Revision = 1;
            about.MinInterfaceVersion = MinInterfaceVersion;
            about.MinApiRevision = MinApiRevision;
            about.ReceiveNotifications = ReceiveNotificationFlags.StartupOnly;
            about.ConfigurationPanelHeight = 150;

            return about;
        }

        public bool Configure(IntPtr panelHandle)
        {
            if (panelHandle == IntPtr.Zero)
            {
                return false;
            }

            var configPanel = (Panel)Panel.FromHandle(panelHandle);
            configPanel.Controls.Clear();

            var textColor = configPanel.ForeColor;

            var sourcesHeader = new Label
            {
                Text = "Lyric Providers:",
                Font = new Font(configPanel.Font, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(4, 4),
                ForeColor = textColor
            };

            var flowLayout = new FlowLayoutPanel
            {
                Location = new Point(4, 24),
                Width = 520,
                Height = 65,
                AutoScroll = true,
                Padding = new Padding(2),
                BackColor = Color.Transparent,
                ForeColor = textColor
            };

            providerCheckBoxes = new CheckBox[LyricProviders.Length];
            for (var i = 0; i < LyricProviders.Length; i++)
            {
                var provider = LyricProviders[i];
                var checkBox = new CheckBox
                {
                    Text = provider.Name,
                    AutoSize = true,
                    Checked = Settings.IsEnabled(provider.Name),
                    Margin = new Padding(4, 4, 12, 4),
                    ForeColor = textColor
                };
                providerCheckBoxes[i] = checkBox;
                flowLayout.Controls.Add(checkBox);
            }

            var thresholdsPanel = new FlowLayoutPanel
            {
                Location = new Point(4, 94),
                Width = 520,
                Height = 35,
                AutoSize = true,
                WrapContents = false,
                BackColor = Color.Transparent,
                ForeColor = textColor
            };

            var titleLabel = new Label
            {
                Text = "Title similarity (%):",
                AutoSize = true,
                Margin = new Padding(4, 6, 4, 0),
                ForeColor = textColor
            };

            titleThresholdUpDown = new NumericUpDown
            {
                Minimum = 50,
                Maximum = 100,
                Value = Settings.TitleMatchThresholdPercent,
                Width = 60,
                Margin = new Padding(0, 2, 24, 0)
            };

            var artistLabel = new Label
            {
                Text = "Artist similarity (%):",
                AutoSize = true,
                Margin = new Padding(0, 6, 4, 0),
                ForeColor = textColor
            };

            artistThresholdUpDown = new NumericUpDown
            {
                Minimum = 50,
                Maximum = 100,
                Value = Settings.ArtistMatchThresholdPercent,
                Width = 60,
                Margin = new Padding(0, 2, 24, 0)
            };

            syncedLyricsCheckBox = new CheckBox
            {
                Text = "Prioritize synced lyrics (LRC)",
                AutoSize = true,
                Checked = Settings.EnableSyncedLyrics,
                Margin = new Padding(0, 6, 0, 0),
                ForeColor = textColor
            };

            thresholdsPanel.Controls.AddRange(new Control[]
            {
                titleLabel, titleThresholdUpDown, artistLabel, artistThresholdUpDown, syncedLyricsCheckBox
            });

            configPanel.Controls.AddRange(new Control[]
            {
                sourcesHeader, flowLayout, thresholdsPanel
            });

            return false;
        }

        public void SaveSettings()
        {
            if (providerCheckBoxes != null)
            {
                foreach (var checkBox in providerCheckBoxes)
                {
                    var entry = Settings.Providers.FirstOrDefault(p => string.Equals(p.Name, checkBox.Text, StringComparison.OrdinalIgnoreCase));
                    if (entry != null)
                    {
                        entry.Enabled = checkBox.Checked;
                    }
                }
            }

            if (titleThresholdUpDown != null)
            {
                Settings.TitleMatchThresholdPercent = (int)titleThresholdUpDown.Value;
            }
            if (artistThresholdUpDown != null)
            {
                Settings.ArtistMatchThresholdPercent = (int)artistThresholdUpDown.Value;
            }
            if (syncedLyricsCheckBox != null)
            {
                Settings.EnableSyncedLyrics = syncedLyricsCheckBox.Checked;
            }

            Settings.Save(settingsDirectory);
        }

        public void Close(PluginCloseReason reason)
        {
        }

        public void Uninstall()
        {
        }

        public void ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
        }
    }
}
