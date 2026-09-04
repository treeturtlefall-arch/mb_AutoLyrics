using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MusicBeePlugin.Common;
using MusicBeePlugin.Matching;

namespace MusicBeePlugin.Providers
{
    public class UtaTenProvider : ILyricsProvider
    {
        private static readonly Encoding SiteEncoding = new UTF8Encoding(false);

        private static readonly Regex SearchResultRegex = new Regex(
            @"<p class=""searchResult__title"">\s*<a href=""/lyric/(?<lyricId>[^""/]+)/"">\s*(?<title>.*?)\s*</a>.*?<td class=""searchResult__artist"">\s*<p>\s*<a href=""/artist/[^""]*"">\s*(?<artist>.*?)\s*</a>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        public string Name => "うたてん";

        public string FetchLyrics(string artist, string trackTitle, string album)
        {
            if (string.IsNullOrWhiteSpace(trackTitle))
            {
                return null;
            }

            using (var client = CreateClient())
            {
                var lyricId = SearchLyricId(client, trackTitle, artist);
                if (lyricId == null)
                {
                    return null;
                }

                var lyricPage = client.DownloadString($"https://utaten.com/lyric/{lyricId}/");
                return ExtractLyrics(lyricPage);
            }
        }

        private static WebClientEx CreateClient()
        {
            var client = new WebClientEx { Encoding = SiteEncoding };
            client.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
            client.Headers[HttpRequestHeader.Referer] = "https://utaten.com/";
            return client;
        }

        private static string SearchLyricId(WebClientEx client, string trackTitle, string artist)
        {
            var searchTerm = TextMatcher.CleanSearchTitle(trackTitle);
            var requestUrl = $"https://utaten.com/lyric/search?title={Uri.EscapeDataString(searchTerm)}&sort=popular_sort_asc";
            var searchPage = client.DownloadString(requestUrl);

            Match bestTitleMatch = null;
            foreach (Match m in SearchResultRegex.Matches(searchPage).Cast<Match>())
            {
                var candidateArtist = WebUtility.HtmlDecode(Regex.Replace(m.Groups["artist"].Value, @"<[^>]+>", string.Empty)).Trim();
                var candidateTitle = WebUtility.HtmlDecode(Regex.Replace(m.Groups["title"].Value, @"<[^>]+>", string.Empty)).Trim();

                var titleThreshold = Plugin.Settings.TitleMatchThresholdPercent / 100.0;
                var titleMatched = string.Equals(candidateTitle, trackTitle, StringComparison.OrdinalIgnoreCase)
                    || TextMatcher.IsSimilar(candidateTitle, trackTitle, titleThreshold)
                    || TextMatcher.IsSimilar(candidateTitle, searchTerm, titleThreshold);

                if (!titleMatched)
                {
                    continue;
                }

                if (bestTitleMatch == null || m.Index < bestTitleMatch.Index)
                {
                    bestTitleMatch = m;
                }

                var artistThreshold = Plugin.Settings.ArtistMatchThresholdPercent / 100.0;
                var artistMatched = string.IsNullOrEmpty(artist)
                    || string.Equals(candidateArtist, artist, StringComparison.OrdinalIgnoreCase)
                    || TextMatcher.IsSimilar(candidateArtist, artist, artistThreshold);

                if (artistMatched)
                {
                    return m.Groups["lyricId"].Value.Trim();
                }
            }

            if (string.IsNullOrEmpty(artist) && bestTitleMatch != null)
            {
                return bestTitleMatch.Groups["lyricId"].Value.Trim();
            }

            return null;
        }

        private static string ExtractLyrics(string html)
        {
            var bodyIndex = html.IndexOf("lyricBody", StringComparison.Ordinal);
            if (bodyIndex < 0)
            {
                return null;
            }

            var mediumIndex = html.IndexOf("class=\"medium\"", bodyIndex, StringComparison.Ordinal);
            if (mediumIndex < 0)
            {
                return null;
            }

            var divStart = html.IndexOf('>', mediumIndex);
            if (divStart < 0)
            {
                return null;
            }
            divStart++;

            var end = FindMatchingDivClose(html, divStart - 1);
            if (end < 0)
            {
                return null;
            }

            var raw = html.Substring(divStart, end - divStart);
            var lyrics = HtmlCleaner.CleanToPlainText(raw, removeRuby: true);
            return string.IsNullOrWhiteSpace(lyrics) ? null : lyrics;
        }

        private static int FindMatchingDivClose(string html, int openTagStart)
        {
            var depth = 0;
            foreach (Match m in Regex.Matches(html.Substring(openTagStart), @"<div\b|</div>"))
            {
                if (m.Value.StartsWith("<div", StringComparison.Ordinal))
                {
                    depth++;
                }
                else
                {
                    depth--;
                    if (depth == 0)
                    {
                        return openTagStart + m.Index;
                    }
                }
            }
            return -1;
        }
    }
}
