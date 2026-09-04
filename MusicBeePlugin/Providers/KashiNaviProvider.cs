using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MusicBeePlugin.Common;
using MusicBeePlugin.Matching;

namespace MusicBeePlugin.Providers
{
    public class KashiNaviProvider : ILyricsProvider, IDebuggableProvider
    {
        private static readonly Encoding SiteEncoding = Encoding.GetEncoding("shift_jis");

        private static double ArtistMatchThreshold => Plugin.Settings.ArtistMatchThresholdPercent / 100.0;
        private static double TitleMatchThreshold => Plugin.Settings.TitleMatchThresholdPercent / 100.0;

        private static readonly Regex SearchResultRegex = new Regex(
            @"<td style=""width:200px"">(?:(?<newmark><img[^>]*>)\s*<br>)?(?:<a href=""/lyrics/(?<lyricId>\d+)/"">)(?<title>[^<]+)</a></td>\s*<td[^>]*><a href=""/artist/[^""]*"">(?<artist>[^<]+)</a>",
            RegexOptions.Compiled);

        private static readonly Regex[] LyricsBlockRegexes =
        {
            new Regex(
                @"<div[^>]*class=""kashi-japanese-block""[^>]*>(?<lyric>.*?)</div>",
                RegexOptions.Singleline | RegexOptions.Compiled),
            new Regex(
                @"<div[^>]*class=""kashi""[^>]*>(?<lyric>.*?)</div>",
                RegexOptions.Singleline | RegexOptions.Compiled),
            new Regex(
                @"<div[^>]*style=""user-select:none[^""]*""[^>]*>(?<lyric>.*?)</div>",
                RegexOptions.Singleline | RegexOptions.Compiled)
        };

        public string Name => "歌詞ナビ";

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

                var lyricPage = client.DownloadString($"https://kashinavi.com/lyrics/{lyricId}/");
                var lyrics = LyricsBlockRegexes
                    .Select(regex => regex.Match(lyricPage))
                    .Where(m => m.Success)
                    .Select(m => HtmlCleaner.CleanToPlainText(m.Groups["lyric"].Value))
                    .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

                return lyrics;
            }
        }

        public System.Collections.Generic.IEnumerable<string> DebugSearch(string artist, string trackTitle)
        {
            using (var client = CreateClient())
            {
                var requestUrl = $"https://kashinavi.com/search.php?r=kyoku&search={EncodeQuery(trackTitle)}&m=bubun&start=1";
                yield return $"request: {requestUrl}";
                var searchPage = client.DownloadString(requestUrl);
                yield return $"page length: {searchPage.Length}";
                yield return $"no-data marker: {searchPage.Contains("該当データがありませんでした。")}";
                var matches = SearchResultRegex.Matches(searchPage);
                yield return $"regex hits: {matches.Count}";

                foreach (Match m in matches.Cast<Match>().Take(5))
                {
                    yield return $"candidate: id={m.Groups["lyricId"].Value} title=[{m.Groups["title"].Value}] artist=[{m.Groups["artist"].Value}]";
                    yield return $"  title equal={string.Equals(m.Groups["title"].Value.Trim(), trackTitle, StringComparison.OrdinalIgnoreCase)} similar={TextMatcher.IsSimilar(m.Groups["title"].Value.Trim(), trackTitle, TitleMatchThreshold)}";
                    yield return $"  artist equal={string.Equals(m.Groups["artist"].Value.Trim(), artist, StringComparison.OrdinalIgnoreCase)} similar={TextMatcher.IsSimilar(m.Groups["artist"].Value.Trim(), artist, ArtistMatchThreshold)}";
                }
            }
        }

        private static WebClientEx CreateClient()
        {
            var client = new WebClientEx { Encoding = SiteEncoding };
            client.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
            client.Headers[HttpRequestHeader.Referer] = "https://kashinavi.com/";
            return client;
        }

        private static string SearchLyricId(WebClientEx client, string trackTitle, string artist)
        {
            var searchTerm = TextMatcher.CleanSearchTitle(trackTitle);
            var search = EncodeQuery(searchTerm);
            var requestUrl = $"https://kashinavi.com/search.php?r=kyoku&search={search}&m=bubun&start=1";
            var searchPage = client.DownloadString(requestUrl);

            if (searchPage.Contains("該当データがありませんでした。"))
            {
                return null;
            }

            Match bestTitleMatch = null;
            foreach (Match m in SearchResultRegex.Matches(searchPage).Cast<Match>())
            {
                var candidateArtist = WebUtility.HtmlDecode(m.Groups["artist"].Value).Trim();
                var candidateTitle = WebUtility.HtmlDecode(m.Groups["title"].Value).Trim();

                var titleMatched = string.Equals(candidateTitle, trackTitle, StringComparison.OrdinalIgnoreCase)
                    || TextMatcher.IsSimilar(candidateTitle, trackTitle, TitleMatchThreshold);

                if (!titleMatched)
                {
                    continue;
                }

                if (bestTitleMatch == null || m.Index < bestTitleMatch.Index)
                {
                    bestTitleMatch = m;
                }

                var artistMatched = string.IsNullOrEmpty(artist)
                    || string.Equals(candidateArtist, artist, StringComparison.OrdinalIgnoreCase)
                    || TextMatcher.IsSimilar(candidateArtist, artist, ArtistMatchThreshold);

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

        private static string EncodeQuery(string value)
        {
            return string.Concat(SiteEncoding.GetBytes(value).Select(b => $"%{b:X2}"));
        }
    }
}
