using System;
using System.Linq;
using System.Text;
using MusicBeePlugin.Providers;

namespace LyricsTester
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Tls11 | System.Net.SecurityProtocolType.Tls;
            }
            catch { }

            MusicBeePlugin.Providers.OriconProvider.SetCacheDirectory(System.IO.Path.GetTempPath());

            if (args.Length == 0)
            {
                Console.WriteLine("usage: LyricsTester <artist> <title> [album] [--provider <name>] [--synced] [--debug]");
                Console.WriteLine("       LyricsTester --list");
                return;
            }

            var providers = ProviderRegistry.CreateAllProviders();

            if (args[0] == "--list")
            {
                foreach (var p in providers)
                {
                    Console.WriteLine(p.Name);
                }
                return;
            }

            var flagIndices = new System.Collections.Generic.HashSet<int>();
            var providerArg = Array.IndexOf(args, "--provider");
            if (providerArg >= 0 && providerArg + 1 < args.Length)
            {
                flagIndices.Add(providerArg);
                flagIndices.Add(providerArg + 1);
                providers = providers.Where(p => string.Equals(p.Name, args[providerArg + 1], StringComparison.OrdinalIgnoreCase)).ToArray();
            }

            var synced = args.Contains("--synced");
            var syncedIdx = Array.IndexOf(args, "--synced");
            if (syncedIdx >= 0) flagIndices.Add(syncedIdx);

            var debug = args.Contains("--debug");
            var debugIdx = Array.IndexOf(args, "--debug");
            if (debugIdx >= 0) flagIndices.Add(debugIdx);

            var positional = args.Select((a, i) => (a, i)).Where(x => !flagIndices.Contains(x.i)).Select(x => x.a).ToArray();
            if (positional.Length < 2)
            {
                Console.WriteLine("error: artist and title are required.");
                return;
            }

            var artist = positional[0];
            var title = positional[1];
            var album = positional.Length > 2 ? positional[2] : "";
            if (debug)
            {
                foreach (var p in providers.OfType<MusicBeePlugin.Providers.IDebuggableProvider>())
                {
                    foreach (var line in p.DebugSearch(artist, title))
                    {
                        Console.WriteLine($"[debug] {line}");
                    }
                }
            }

            foreach (var provider in providers)
            {
                Console.WriteLine($"=== {provider.Name} ===");
                try
                {
                    string lyrics;
                    if (synced && provider is MusicBeePlugin.Providers.ISyncedLyricsAwareProvider syncAware)
                    {
                        lyrics = syncAware.FetchLyrics(artist, title, album, null, preferSynchronized: true);
                    }
                    else
                    {
                        lyrics = provider.FetchLyrics(artist, title, album);
                    }

                    if (lyrics == null)
                    {
                        Console.WriteLine("(not found)");
                        continue;
                    }
                    Console.WriteLine(lyrics);
                }
                catch (System.Net.WebException we)
                {
                    var message = $"ERROR: {we.Message}";
                    var response = we.Response as System.Net.HttpWebResponse;
                    if (response != null)
                    {
                        message += $" | status={response.StatusCode}";
                        try
                        {
                            using (var stream = response.GetResponseStream())
                            using (var reader = new System.IO.StreamReader(stream, Encoding.UTF8))
                            {
                                var body = reader.ReadToEnd();
                                if (!string.IsNullOrEmpty(body))
                                {
                                    message += $" | body={body.Substring(0, Math.Min(300, body.Length))}";
                                }
                            }
                        }
                        catch { }
                    }
                    Console.WriteLine(message);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"ERROR: {e.Message}");
                }
            }
        }
    }
}
