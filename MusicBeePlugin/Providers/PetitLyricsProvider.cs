using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MusicBeePlugin.Common;
using MusicBeePlugin.Matching;

namespace MusicBeePlugin.Providers
{
    public class PetitLyricsProvider : ILyricsProvider, IDebuggableProvider
    {
        private static readonly Encoding SiteEncoding = new UTF8Encoding(false);

        private static readonly Regex SearchResultRegex = new Regex(
            @"<a href=""/lyrics/(?<lyricId>\d+)""><span class=""lyrics-list-title"">(?<title>.*?)</span></a>\s*<br>\s*<a href=""/lyrics/artist/\d+""><span class=""lyrics-list-artist"">(?<artist>.*?)</span>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex CsrfTokenRegex = new Regex(
            @"'X-CSRF-Token', '(?<token>[^']+)",
            RegexOptions.Compiled);

        private static readonly Regex LyricsJsonRegex = new Regex(
            @"""lyrics"":""(?<data>[A-Za-z0-9+/=]*)""",
            RegexOptions.Compiled);

        public string Name => "プチリリ";

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

                var lyricPage = $"https://petitlyrics.com/lyrics/{lyricId}";
                client.DownloadData(lyricPage);

                var libJs = client.DownloadString("https://petitlyrics.com/lib/pl-lib.js");
                var tokenMatch = CsrfTokenRegex.Match(libJs);
                if (!tokenMatch.Success)
                {
                    return null;
                }

                client.Headers.Clear();
                client.Headers[HttpRequestHeader.Accept] = "*/*";
                client.Headers[HttpRequestHeader.AcceptLanguage] = "ja";
                client.Headers[HttpRequestHeader.Referer] = lyricPage;
                client.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
                client.Headers[HttpRequestHeader.Pragma] = "no-cache";
                client.Headers[HttpRequestHeader.ContentType] = "application/x-www-form-urlencoded; charset=UTF-8";
                client.Headers.Add("X-Requested-With", "XMLHttpRequest");
                client.Headers.Add("X-CSRF-Token", tokenMatch.Groups["token"].Value);

                var json = client.UploadString("https://petitlyrics.com/com/get_lyrics.ajax", $"lyrics_id={lyricId}");

                var lyrics = DecodeLyricsJson(json);
                return string.IsNullOrWhiteSpace(lyrics) ? null : lyrics;
            }
        }

        public System.Collections.Generic.IEnumerable<string> DebugSearch(string artist, string trackTitle)
        {
            using (var client = CreateClient())
            {
                var searchTerm = TextMatcher.CleanSearchTitle(trackTitle);
                var requestUrl = !string.IsNullOrWhiteSpace(artist)
                    ? $"https://petitlyrics.com/search_lyrics?title={Uri.EscapeDataString(searchTerm)}&artist={Uri.EscapeDataString(artist)}"
                    : $"https://petitlyrics.com/search_lyrics?title={Uri.EscapeDataString(searchTerm)}";

                yield return $"search url: {requestUrl}";
                var searchPage = client.DownloadString(requestUrl);
                yield return $"search page length: {searchPage.Length}";
                var matches = SearchResultRegex.Matches(searchPage);
                yield return $"regex hits: {matches.Count}";
                foreach (Match m in matches.Cast<Match>().Take(5))
                {
                    yield return $"  id={m.Groups["lyricId"].Value} title=[{WebUtility.HtmlDecode(m.Groups["title"].Value).Trim()}] artist=[{WebUtility.HtmlDecode(m.Groups["artist"].Value).Trim()}]";
                }
            }
        }

        private static WebClientEx CreateClient()
        {
            var client = new WebClientEx { Encoding = SiteEncoding };
            return client;
        }

        private static string SearchLyricId(WebClientEx client, string trackTitle, string artist)
        {
            var searchTerm = TextMatcher.CleanSearchTitle(trackTitle);

            if (!string.IsNullOrWhiteSpace(artist))
            {
                var urlWithArtist = $"https://petitlyrics.com/search_lyrics?title={Uri.EscapeDataString(searchTerm)}&artist={Uri.EscapeDataString(artist)}";
                var id = SearchLyricIdFromUrl(client, urlWithArtist, trackTitle, artist, searchTerm);
                if (id != null)
                {
                    return id;
                }
            }

            var urlTitleOnly = $"https://petitlyrics.com/search_lyrics?title={Uri.EscapeDataString(searchTerm)}";
            return SearchLyricIdFromUrl(client, urlTitleOnly, trackTitle, artist, searchTerm);
        }

        private static string SearchLyricIdFromUrl(WebClientEx client, string requestUrl, string trackTitle, string artist, string searchTerm)
        {
            string searchPage;
            try
            {
                searchPage = client.DownloadString(requestUrl);
            }
            catch (WebException)
            {
                return null;
            }

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
                    return m.Groups["lyricId"].Value.Trim();
                }
            }

            if (string.IsNullOrEmpty(artist) && bestTitleMatch != null)
            {
                return bestTitleMatch.Groups["lyricId"].Value.Trim();
            }

            return null;
        }

        private static string DecodeLyricsJson(string json)
        {
            var lines = LyricsJsonRegex.Matches(json)
                .Cast<Match>()
                .Select(m => Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups["data"].Value)).TrimEnd('\r', '\n'));

            var text = string.Join("\n", lines);
            return HtmlCleaner.CleanToPlainText(text);
        }
    }
}
