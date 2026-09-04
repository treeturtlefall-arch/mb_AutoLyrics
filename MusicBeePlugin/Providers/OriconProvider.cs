using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using MusicBeePlugin.Common;
using MusicBeePlugin.Matching;

namespace MusicBeePlugin.Providers
{
    public class OriconProvider : ILyricsProvider, IDebuggableProvider
    {
        private const int SearchMinIntervalMs = 3000;
        private const int MaxArtistCandidates = 3;

        private static readonly object SearchLock = new object();
        private static DateTime _lastSearchRequest = DateTime.MinValue;

        private static readonly Encoding SiteEncoding = Encoding.GetEncoding("Shift_JIS");

        private static readonly Regex BraveResultRegex = new Regex(
            @"oricon\.co\.jp/prof/(?<artistId>\d+)/lyrics/",
            RegexOptions.Compiled);

        private static readonly Regex SongListRegex = new Regex(
            @"<a href=""/prof/\d+/lyrics/(?<songId>\d+)/"">.*?<span class=""title"">(?<title>.*?)</span>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex LyricsBlockRegex = new Regex(
            @"<div class=""all-lyrics""[^>]*>\s*<p>(?<lyric>.*?)</p>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        public string Name => "オリコン";

        public string FetchLyrics(string artist, string trackTitle, string album)
        {
            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(trackTitle))
            {
                return null;
            }

            using (var client = CreateClient())
            {
                var artistId = ResolveArtistId(client, artist);
                if (artistId == null)
                {
                    return null;
                }

                var songId = SearchSongIdWithPagination(client, artistId, trackTitle);
                if (songId == null)
                {
                    return null;
                }

                var songPage = client.DownloadString($"https://www.oricon.co.jp/prof/{artistId}/lyrics/{songId}/");
                var match = LyricsBlockRegex.Match(songPage);
                if (!match.Success)
                {
                    return null;
                }

                var lyrics = HtmlCleaner.CleanToPlainText(match.Groups["lyric"].Value);
                return string.IsNullOrWhiteSpace(lyrics) ? null : lyrics;
            }
        }

        public System.Collections.Generic.IEnumerable<string> DebugSearch(string artist, string trackTitle)
        {
            using (var client = CreateClient())
            {
                var query = Uri.EscapeDataString($"site:oricon.co.jp/prof {artist} 歌詞");
                var searchPage = CurlHelper.Get($"https://search.brave.com/search?q={query}");
                yield return $"brave page length: {(searchPage?.Length ?? 0)}";
                var matches = BraveResultRegex.Matches(searchPage ?? string.Empty);
                yield return $"brave regex hits: {matches.Count}";
                foreach (var id in matches.Cast<Match>().Select(m => m.Groups["artistId"].Value).Distinct().Take(5))
                {
                    yield return $"  artistId={id}";
                }

                var artistId = ResolveArtistId(client, artist);
                yield return $"resolved artistId: {artistId ?? "(null)"}";
                if (artistId == null) yield break;

                var listPage = client.DownloadString($"https://www.oricon.co.jp/prof/{artistId}/lyrics/title/");
                yield return $"list page length: {listPage.Length}";
                var songMatches = SongListRegex.Matches(listPage);
                yield return $"song rows: {songMatches.Count}";
                foreach (Match m in songMatches.Cast<Match>().Take(5))
                {
                    yield return $"  song: {m.Groups["songId"].Value} [{WebUtility.HtmlDecode(m.Groups["title"].Value).Trim()}]";
                }
            }
        }

        private static WebClientEx CreateClient()
        {
            var client = new WebClientEx { Encoding = SiteEncoding };
            client.Headers[HttpRequestHeader.Referer] = "https://www.oricon.co.jp/";
            return client;
        }

        private static string ResolveArtistId(WebClientEx client, string artist)
        {
            var cached = TryCacheGet(artist);
            if (cached != null)
            {
                return cached;
            }

            var query = Uri.EscapeDataString($"site:oricon.co.jp/prof {artist} 歌詞");

            string searchPage;
            lock (SearchLock)
            {
                var elapsed = (DateTime.Now - _lastSearchRequest).TotalMilliseconds;
                if (elapsed < SearchMinIntervalMs)
                {
                    Thread.Sleep(SearchMinIntervalMs - (int)elapsed);
                }
                searchPage = CurlHelper.Get($"https://search.brave.com/search?q={query}");
                if (string.IsNullOrEmpty(searchPage))
                {
                    Thread.Sleep(5000);
                    searchPage = CurlHelper.Get($"https://search.brave.com/search?q={query}");
                }
                _lastSearchRequest = DateTime.Now;
            }

            if (string.IsNullOrEmpty(searchPage))
            {
                return null;
            }

            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (Match m in BraveResultRegex.Matches(searchPage).Cast<Match>())
            {
                var artistId = m.Groups["artistId"].Value;

                if (!seen.Add(artistId))
                {
                    continue;
                }

                if (seen.Count > MaxArtistCandidates)
                {
                    break;
                }

                if (ArtistPageMatches(client, artistId, artist))
                {
                    CachePut(artist, artistId);
                    return artistId;
                }
            }

            return null;
        }

        private static readonly object CacheLock = new object();
        private static Dictionary<string, string> _artistIdCache;
        private static string _cacheDirectory;

        public static void SetCacheDirectory(string directory)
        {
            lock (CacheLock)
            {
                _cacheDirectory = directory;
                LoadCache();
            }
        }

        private static string GetCacheFilePath()
        {
            return Path.Combine(_cacheDirectory ?? string.Empty, "oricon_artists.cache");
        }

        private static void LoadCache()
        {
            _artistIdCache = new Dictionary<string, string>();
            try
            {
                var path = GetCacheFilePath();
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        var sep = line.IndexOf('\t');
                        if (sep > 0 && sep < line.Length - 1)
                        {
                            _artistIdCache[line.Substring(0, sep)] = line.Substring(sep + 1);
                        }
                    }
                }
            }
            catch (Exception)
            {
                _artistIdCache = new Dictionary<string, string>();
            }
        }

        private static void SaveCache()
        {
            try
            {
                Directory.CreateDirectory(_cacheDirectory);
                File.WriteAllLines(GetCacheFilePath(), _artistIdCache.Select(kv => $"{kv.Key}\t{kv.Value}"));
            }
            catch (Exception)
            {
            }
        }

        private static string TryCacheGet(string artist)
        {
            lock (CacheLock)
            {
                if (_artistIdCache == null)
                {
                    return null;
                }
                return _artistIdCache.TryGetValue(TextMatcher.Normalize(artist), out var id) ? id : null;
            }
        }

        private static void CachePut(string artist, string artistId)
        {
            lock (CacheLock)
            {
                if (_artistIdCache == null)
                {
                    return;
                }
                _artistIdCache[TextMatcher.Normalize(artist)] = artistId;
                SaveCache();
            }
        }

        private static readonly Regex ArtistNameInTitleRegex = new Regex(
            @"<title>\s*(?<artist>.*?)(?:の歌詞|\s*\|)",
            RegexOptions.Compiled);

        private static readonly Regex ArtistNameInH1Regex = new Regex(
            @"<h1[^>]*class=""[^""]*ttl-b[^""]*""[^>]*>\s*<span>\s*(?<artist>.*?)(?:の歌詞|</span>)",
            RegexOptions.Compiled);

        private static bool ArtistPageMatches(WebClientEx client, string artistId, string artist)
        {
            try
            {
                var listPage = client.DownloadString($"https://www.oricon.co.jp/prof/{artistId}/lyrics/title/");

                var mTitle = ArtistNameInTitleRegex.Match(listPage);
                if (mTitle.Success)
                {
                    var pageArtist = WebUtility.HtmlDecode(mTitle.Groups["artist"].Value).Trim();
                    if (TextMatcher.IsSimilar(pageArtist, artist, 0.85))
                    {
                        return true;
                    }
                }

                var mH1 = ArtistNameInH1Regex.Match(listPage);
                if (mH1.Success)
                {
                    var pageArtist = WebUtility.HtmlDecode(mH1.Groups["artist"].Value).Trim();
                    if (TextMatcher.IsSimilar(pageArtist, artist, 0.85))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (WebException)
            {
                return false;
            }
        }

        private const int MaxListPages = 8;

        private static string SearchSongIdWithPagination(WebClientEx client, string artistId, string trackTitle)
        {
            var listUrl = $"https://www.oricon.co.jp/prof/{artistId}/lyrics/title/";
            for (var page = 1; page <= MaxListPages; page++)
            {
                string listPage;
                try
                {
                    listPage = client.DownloadString(page == 1 ? listUrl : $"{listUrl}p/{page}/");
                }
                catch (WebException)
                {
                    return null;
                }

                if (SongListRegex.Matches(listPage).Count == 0)
                {
                    return null;
                }

                var songId = SearchSongId(listPage, trackTitle);
                if (songId != null)
                {
                    return songId;
                }
            }

            return null;
        }

        private static string SearchSongId(string listPage, string trackTitle)
        {
            var searchTerm = TextMatcher.CleanSearchTitle(trackTitle);
            var titleThreshold = Plugin.Settings.TitleMatchThresholdPercent / 100.0;
            foreach (Match m in SongListRegex.Matches(listPage).Cast<Match>())
            {
                var candidateTitle = WebUtility.HtmlDecode(m.Groups["title"].Value).Trim();

                var titleMatched = string.Equals(candidateTitle, trackTitle, StringComparison.OrdinalIgnoreCase)
                    || TextMatcher.IsSimilar(candidateTitle, trackTitle, titleThreshold)
                    || TextMatcher.IsSimilar(candidateTitle, searchTerm, titleThreshold);

                if (titleMatched)
                {
                    return m.Groups["songId"].Value.Trim();
                }
            }

            return null;
        }
    }
}
