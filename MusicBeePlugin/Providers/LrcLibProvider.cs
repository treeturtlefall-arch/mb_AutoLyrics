using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using MusicBeePlugin.Common;
using MusicBeePlugin.Matching;

namespace MusicBeePlugin.Providers
{
    public class LrcLibProvider : ILyricsProvider, IDurationAwareProvider, ISyncedLyricsAwareProvider, IDebuggableProvider
    {
        private const string ApiBase = "https://lrclib.net/api";
        private const string LrcUserAgent = "mb_AutoLyrics/0.4.0 (https://github.com/treeturtlefall-arch/mb_AutoLyrics)";

        private class LrcResult
        {
            public string trackName { get; set; }
            public string artistName { get; set; }
            public double? duration { get; set; }
            public bool instrumental { get; set; }
            public string plainLyrics { get; set; }
            public string syncedLyrics { get; set; }
        }

        public string Name => "LRCLIB";

        public string FetchLyrics(string artist, string trackTitle, string album)
        {
            return FetchLyrics(artist, trackTitle, album, null, false);
        }

        public string FetchLyrics(string artist, string trackTitle, string album, double? durationSeconds)
        {
            return FetchLyrics(artist, trackTitle, album, durationSeconds, false);
        }

        public string FetchLyrics(string artist, string trackTitle, string album, double? durationSeconds, bool preferSynchronized)
        {
            if (string.IsNullOrWhiteSpace(trackTitle))
            {
                return null;
            }

            var direct = TryGetLyrics(BuildGetUrl(artist, trackTitle, album, durationSeconds))
                ?? TryGetLyrics(BuildGetUrl(artist, TextMatcher.CleanSearchTitle(trackTitle), album, durationSeconds));
            if (direct != null)
            {
                return ExtractLyrics(direct, preferSynchronized);
            }

            var searchUrl = $"{ApiBase}/search?track_name={Uri.EscapeDataString(TextMatcher.CleanSearchTitle(trackTitle))}"
                + (string.IsNullOrWhiteSpace(artist) ? "" : $"&artist_name={Uri.EscapeDataString(artist)}");

            var candidates = DownloadResults(searchUrl);
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            var best = PickBest(candidates, artist, trackTitle, durationSeconds);
            if (best == null)
            {
                return null;
            }

            return ExtractLyrics(best, preferSynchronized);
        }

        private static string ExtractLyrics(LrcResult result, bool preferSynchronized)
        {
            if (result == null || result.instrumental)
            {
                return null;
            }

            if (preferSynchronized && !string.IsNullOrWhiteSpace(result.syncedLyrics))
            {
                return result.syncedLyrics.Trim();
            }

            if (!string.IsNullOrWhiteSpace(result.plainLyrics))
            {
                return result.plainLyrics.Trim();
            }

            if (!string.IsNullOrWhiteSpace(result.syncedLyrics))
            {
                return result.syncedLyrics.Trim();
            }

            return null;
        }

        private static string BuildGetUrl(string artist, string trackTitle, string album, double? durationSeconds)
        {
            var url = $"{ApiBase}/get?artist_name={Uri.EscapeDataString(artist ?? "")}&track_name={Uri.EscapeDataString(trackTitle)}";
            if (!string.IsNullOrWhiteSpace(album))
            {
                url += $"&album_name={Uri.EscapeDataString(album)}";
            }
            if (durationSeconds.HasValue && durationSeconds.Value > 0)
            {
                url += $"&duration={(int)Math.Round(durationSeconds.Value)}";
            }
            return url;
        }

        private static LrcResult TryGetLyrics(string url)
        {
            try
            {
                var json = CurlHelper.Get(url, userAgent: LrcUserAgent);
                if (string.IsNullOrEmpty(json))
                {
                    return null;
                }
                return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<LrcResult>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static List<LrcResult> DownloadResults(string url)
        {
            try
            {
                var json = CurlHelper.Get(url, userAgent: LrcUserAgent);
                if (string.IsNullOrEmpty(json))
                {
                    return null;
                }
                return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<LrcResult>>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static LrcResult PickBest(List<LrcResult> candidates, string artist, string trackTitle, double? durationSeconds)
        {
            var titleThreshold = Plugin.Settings.TitleMatchThresholdPercent / 100.0;
            var artistThreshold = Plugin.Settings.ArtistMatchThresholdPercent / 100.0;

            LrcResult best = null;
            double bestScore = -1;

            foreach (var c in candidates.Where(c => !c.instrumental))
            {
                var titleSim = TextMatcher.Similarity(c.trackName, trackTitle);
                if (titleSim < titleThreshold)
                {
                    continue;
                }

                var artistSim = TextMatcher.Similarity(c.artistName, artist ?? "");
                if (!string.IsNullOrWhiteSpace(artist) && artistSim < artistThreshold)
                {
                    continue;
                }

                double score = titleSim * 2 + artistSim;

                if (durationSeconds.HasValue && c.duration.HasValue)
                {
                    var diff = Math.Abs(c.duration.Value - durationSeconds.Value);
                    if (diff <= 2.0)
                    {
                        score += 1.0;
                    }
                    else if (diff > 10.0)
                    {
                        score -= 1.0;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            return best;
        }

        public IEnumerable<string> DebugSearch(string artist, string trackTitle)
        {
            var getUrl = BuildGetUrl(artist, trackTitle, null, null);
            yield return $"get url: {getUrl}";
            var direct = TryGetLyrics(getUrl);
            yield return direct == null ? "get: no result" : $"get hit: [{direct.trackName}] {direct.artistName} plain:{(direct.plainLyrics?.Length ?? 0)}chars sync:{(direct.syncedLyrics?.Length ?? 0)}chars";

            var cleanTitle = TextMatcher.CleanSearchTitle(trackTitle);
            var cleanUrl = BuildGetUrl(artist, cleanTitle, null, null);
            yield return $"clean get url: {cleanUrl} (clean=[{cleanTitle}])";
            var cleanDirect = TryGetLyrics(cleanUrl);
            yield return cleanDirect == null ? "clean get: no result" : $"clean get hit: [{cleanDirect.trackName}] {cleanDirect.artistName} plain:{(cleanDirect.plainLyrics?.Length ?? 0)}chars sync:{(cleanDirect.syncedLyrics?.Length ?? 0)}chars";

            var searchUrl = $"{ApiBase}/search?track_name={Uri.EscapeDataString(cleanTitle)}"
                + (string.IsNullOrWhiteSpace(artist) ? "" : $"&artist_name={Uri.EscapeDataString(artist)}");
            yield return $"search url: {searchUrl}";

            var candidates = DownloadResults(searchUrl);
            yield return $"search hits: {(candidates?.Count ?? -1)}";
            if (candidates != null)
            {
                foreach (var c in candidates.Take(5))
                {
                    yield return $"  [{c.trackName}] {c.artistName} dur:{(c.duration?.ToString() ?? "-")} plain:{(c.plainLyrics?.Length ?? 0)}chars sync:{(c.syncedLyrics?.Length ?? 0)}chars inst:{c.instrumental}";
                }
            }
        }
    }
}
