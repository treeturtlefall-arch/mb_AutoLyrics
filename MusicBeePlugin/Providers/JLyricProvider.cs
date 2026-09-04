using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MusicBeePlugin.Common;
using MusicBeePlugin.Matching;

namespace MusicBeePlugin.Providers
{
    public class JLyricProvider : ILyricsProvider
    {
        private static readonly Encoding SiteEncoding = new UTF8Encoding(false);

        private static readonly Regex SearchResultRegex = new Regex(
            @"<p class=""mid""><a href=""(?<url>/artist/[^""]+\.html)""[^>]*>(?<title>[^<]+)</a></p>\s*<p class=""sml"">歌：<a href=""/artist/[^""]*""[^>]*>(?<artist>[^<]+)</a>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex LyricsBlockRegex = new Regex(
            @"<p id=""Lyric""[^>]*>(?<lyric>.*?)</p>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        public string Name => "J-Lyric";

        public string FetchLyrics(string artist, string trackTitle, string album)
        {
            if (string.IsNullOrWhiteSpace(trackTitle))
            {
                return null;
            }

            using (var client = CreateClient())
            {
                var songUrl = SearchSongUrl(client, trackTitle, artist);
                if (songUrl == null)
                {
                    return null;
                }

                var lyricPage = client.DownloadString($"https://j-lyric.net{songUrl}");
                var match = LyricsBlockRegex.Match(lyricPage);
                if (!match.Success)
                {
                    return null;
                }

                var lyrics = HtmlCleaner.CleanToPlainText(match.Groups["lyric"].Value, removeRuby: true);
                return string.IsNullOrWhiteSpace(lyrics) ? null : lyrics;
            }
        }

        private static WebClientEx CreateClient()
        {
            var client = new WebClientEx { Encoding = SiteEncoding };
            client.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
            client.Headers[HttpRequestHeader.Referer] = "https://j-lyric.net/";
            return client;
        }

        private static string SearchSongUrl(WebClientEx client, string trackTitle, string artist)
        {
            var searchTerm = TextMatcher.CleanSearchTitle(trackTitle);
            var requestUrl = $"https://j-lyric.net/search.php?kt={Uri.EscapeDataString(searchTerm)}&ct=2";
            var searchPage = client.DownloadString(requestUrl);

            Match bestTitleMatch = null;
            foreach (Match m in SearchResultRegex.Matches(searchPage).Cast<Match>())
            {
                var candidateArtist = WebUtility.HtmlDecode(m.Groups["artist"].Value).Trim();
                var candidateTitle = WebUtility.HtmlDecode(m.Groups["title"].Value).Trim();

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
                    return m.Groups["url"].Value.Trim();
                }
            }

            if (string.IsNullOrEmpty(artist) && bestTitleMatch != null)
            {
                return bestTitleMatch.Groups["url"].Value.Trim();
            }

            return null;
        }
    }
}
