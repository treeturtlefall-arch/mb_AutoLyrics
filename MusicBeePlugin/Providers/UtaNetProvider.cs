using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using MusicBeePlugin.Common;
using MusicBeePlugin.Matching;

namespace MusicBeePlugin.Providers
{
    public class UtaNetProvider : ILyricsProvider
    {
        private static readonly Regex SearchResultRegex = new Regex(
            @"<a href=""/song/(?<songId>\d+)/""[^>]*><span class=""fw-bold songlist-title"">(?<title>.*?)</span>.*?<td class=""sp-none fw-bold""><a href=""/artist/\d+/"">(?<artist>.*?)</a>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex LyricsBlockRegex = new Regex(
            @"<div id=""kashi_area""[^>]*>(?<lyric>.*?)</div>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        public string Name => "歌ネット";

        public string FetchLyrics(string artist, string trackTitle, string album)
        {
            if (string.IsNullOrWhiteSpace(trackTitle))
            {
                return null;
            }

            var searchTerm = TextMatcher.CleanSearchTitle(trackTitle);
            var searchUrl = $"https://www.uta-net.com/search/?Keyword={Uri.EscapeDataString(searchTerm)}&target=tit&type=in";
            var searchPage = CurlHelper.Get(searchUrl);
            if (string.IsNullOrEmpty(searchPage))
            {
                return null;
            }

            var songId = SearchSongId(searchPage, trackTitle, artist, searchTerm);
            if (songId == null)
            {
                return null;
            }

            var songPage = CurlHelper.Get($"https://www.uta-net.com/song/{songId}/");
            if (string.IsNullOrEmpty(songPage))
            {
                return null;
            }

            var match = LyricsBlockRegex.Match(songPage);
            if (!match.Success)
            {
                return null;
            }

            var lyrics = HtmlCleaner.CleanToPlainText(match.Groups["lyric"].Value);
            return string.IsNullOrWhiteSpace(lyrics) ? null : lyrics;
        }

        private static string SearchSongId(string searchPage, string trackTitle, string artist, string searchTerm)
        {
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
                    return m.Groups["songId"].Value.Trim();
                }
            }

            if (string.IsNullOrEmpty(artist) && bestTitleMatch != null)
            {
                return bestTitleMatch.Groups["songId"].Value.Trim();
            }

            return null;
        }
    }
}
