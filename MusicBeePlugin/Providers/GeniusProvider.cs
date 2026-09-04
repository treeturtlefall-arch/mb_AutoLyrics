using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using MusicBeePlugin.Common;
using MusicBeePlugin.Matching;

namespace MusicBeePlugin.Providers
{
    public class GeniusProvider : ILyricsProvider, IDebuggableProvider
    {
        private const string SearchApiBase = "https://genius.com/api/search/multi?q=";
        private const string SiteEncodingName = "utf-8";

        private static readonly Encoding SiteEncoding = new UTF8Encoding(false);

        private static readonly Regex LyricsContainerRegex = new Regex(
            @"<div[^>]*data-lyrics-container=""true""[^>]*>",
            RegexOptions.Compiled);

        private class SearchResponse { public SearchData response { get; set; } }
        private class SearchData { public List<SearchSection> sections { get; set; } }
        private class SearchSection { public List<SearchHit> hits { get; set; } }
        private class SearchHit { public string type { get; set; } public SongResult result { get; set; } }
        private class SongResult
        {
            public string title { get; set; }
            public string url { get; set; }
            public ArtistInfo primary_artist { get; set; }
        }
        private class ArtistInfo { public string name { get; set; } }

        public string Name => "Genius";

        public string FetchLyrics(string artist, string trackTitle, string album)
        {
            if (string.IsNullOrWhiteSpace(trackTitle))
            {
                return null;
            }

            using (var client = CreateClient())
            {
                var songUrl = FindSongUrl(client, artist, trackTitle);
                if (songUrl == null)
                {
                    return null;
                }

                var songPage = client.DownloadString(songUrl);
                return ExtractLyrics(songPage);
            }
        }

        private static WebClientEx CreateClient()
        {
            var client = new WebClientEx { Encoding = SiteEncoding };
            client.Headers[HttpRequestHeader.AcceptLanguage] = "en-US;q=0.9,en;q=0.8";
            return client;
        }

        private static string FindSongUrl(WebClientEx client, string artist, string trackTitle)
        {
            if (string.IsNullOrWhiteSpace(artist))
            {
                return null;
            }

            var cleanTrackTitle = TextMatcher.CleanSearchTitle(trackTitle);
            var query = Uri.EscapeDataString($"{artist} {cleanTrackTitle}");
            var json = client.DownloadString(SearchApiBase + query);

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var data = serializer.Deserialize<SearchResponse>(json);
            var songs = (data?.response?.sections ?? new List<SearchSection>())
                .SelectMany(s => s?.hits ?? new List<SearchHit>())
                .Where(h => h.type == "song" && h.result != null && !string.IsNullOrEmpty(h.result.url))
                .ToList();

            var titleThreshold = Plugin.Settings.TitleMatchThresholdPercent / 100.0;
            var artistThreshold = Plugin.Settings.ArtistMatchThresholdPercent / 100.0;

            SongResult best = null;
            double bestScore = -1;

            foreach (var hit in songs)
            {
                var candidateTitle = hit.result.title ?? "";
                var candidateArtist = hit.result.primary_artist?.name ?? "";

                var titleSim = TextMatcher.Similarity(TextMatcher.CleanSearchTitle(candidateTitle), cleanTrackTitle);
                if (titleSim < titleThreshold)
                {
                    continue;
                }

                var artistSim = TextMatcher.Similarity(candidateArtist, artist ?? "");
                if (!string.IsNullOrWhiteSpace(artist) && artistSim < artistThreshold)
                {
                    continue;
                }

                var score = titleSim * 2 + artistSim;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = hit.result;
                }
            }

            return best?.url;
        }

        public static string ExtractLyrics(string html)
        {
            var cleaned = RemoveBlocksWithMarker(html, "data-exclude-from-selection");

            var sb = new StringBuilder();
            foreach (Match m in LyricsContainerRegex.Matches(cleaned))
            {
                var content = ExtractBalancedDivContent(cleaned, m.Index);
                if (content == null)
                {
                    continue;
                }

                var text = HtmlCleaner.CleanToPlainText(content);
                sb.Append(text);
                sb.Append("\n\n");
            }

            var result = Regex.Replace(sb.ToString(), @"\n{3,}", "\n\n").Trim();
            result = Regex.Replace(result, @"^\[[^\[\]]*歌詞\][ \t]*\r?\n?", string.Empty);
            return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
        }

        private static string ExtractBalancedDivContent(string html, int openTagStart)
        {
            var start = html.IndexOf('>', openTagStart) + 1;
            var pos = start;
            var depth = 1;

            while (depth > 0)
            {
                var open = html.IndexOf("<div", pos, StringComparison.OrdinalIgnoreCase);
                var close = html.IndexOf("</div", pos, StringComparison.OrdinalIgnoreCase);
                if (close < 0)
                {
                    return null;
                }
                if (open >= 0 && open < close)
                {
                    depth++;
                    pos = open + 4;
                }
                else
                {
                    depth--;
                    if (depth == 0)
                    {
                        return html.Substring(start, close - start);
                    }
                    pos = close + 5;
                }
            }

            return null;
        }

        private static string RemoveBlocksWithMarker(string html, string marker)
        {
            var result = html;

            while (true)
            {
                var markerIdx = result.IndexOf(marker, StringComparison.Ordinal);
                if (markerIdx < 0)
                {
                    return result;
                }

                var tagStart = result.LastIndexOf("<div", markerIdx, StringComparison.Ordinal);
                if (tagStart < 0)
                {
                    return result;
                }

                var contentStart = result.IndexOf('>', tagStart) + 1;
                if (contentStart <= 0)
                {
                    return result;
                }

                var pos = contentStart;
                var depth = 1;
                int end = -1;

                while (depth > 0)
                {
                    var open = result.IndexOf("<div", pos, StringComparison.OrdinalIgnoreCase);
                    var close = result.IndexOf("</div", pos, StringComparison.OrdinalIgnoreCase);
                    if (close < 0)
                    {
                        break;
                    }
                    if (open >= 0 && open < close)
                    {
                        depth++;
                        pos = open + 4;
                    }
                    else
                    {
                        depth--;
                        if (depth == 0)
                        {
                            var closeEnd = result.IndexOf('>', close);
                            end = closeEnd >= 0 ? closeEnd + 1 : Math.Min(close + 6, result.Length);
                            break;
                        }
                        pos = close + 5;
                    }
                }

                if (end < 0)
                {
                    end = result.Length;
                }

                var removeCount = Math.Min(end - tagStart, result.Length - tagStart);
                if (removeCount <= 0)
                {
                    break;
                }

                result = result.Remove(tagStart, removeCount);
            }

            return result;
        }

        public IEnumerable<string> DebugSearch(string artist, string trackTitle)
        {
            using (var client = CreateClient())
            {
                var cleanTitle = TextMatcher.CleanSearchTitle(trackTitle);
                var query = Uri.EscapeDataString($"{artist} {cleanTitle}");
                yield return $"query: {artist} {cleanTitle}";

                var json = client.DownloadString(SearchApiBase + query);
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var data = serializer.Deserialize<SearchResponse>(json);
                var songs = (data?.response?.sections ?? new List<SearchSection>())
                    .SelectMany(s => s?.hits ?? new List<SearchHit>())
                    .Where(h => h.type == "song" && h.result != null)
                    .ToList();

                yield return $"song hits: {songs.Count}";
                foreach (var hit in songs.Take(5))
                {
                    yield return $"  [{hit.result.title}] {hit.result.primary_artist?.name} -> {hit.result.url}";
                }

                var url = FindSongUrl(client, artist, trackTitle);
                yield return $"selected: {url ?? "(none)"}";
                if (url != null)
                {
                    var page = client.DownloadString(url);
                    var lyrics = ExtractLyrics(page);
                    yield return $"lyrics: {(lyrics == null ? "(null)" : $"{lyrics.Length} chars")}";
                }
            }
        }
    }
}
