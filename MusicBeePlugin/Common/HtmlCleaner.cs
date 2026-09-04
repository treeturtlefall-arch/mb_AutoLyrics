using System;
using System.Net;
using System.Text.RegularExpressions;

namespace MusicBeePlugin.Common
{
    public static class HtmlCleaner
    {
        private static readonly Regex RubyPattern = new Regex(@"<r[pt][^>]*>.*?</r[pt]>|<span\b[^>]*class=[""'][^""']*?\brt\b[^""']*?[""'][^>]*>.*?</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex BrPattern = new Regex(@"\s*<br\s*/?\s*>\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ParagraphPattern = new Regex(@"</?p\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TagPattern = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex TrailingSpacePattern = new Regex(@"[ \t]+\n", RegexOptions.Compiled);
        private static readonly Regex MultiNewlinePattern = new Regex(@"\n{3,}", RegexOptions.Compiled);

        public static string CleanToPlainText(string html, bool removeRuby = true)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return string.Empty;
            }

            var text = html;
            if (removeRuby)
            {
                text = RubyPattern.Replace(text, string.Empty);
            }

            text = BrPattern.Replace(text, "\n");
            text = ParagraphPattern.Replace(text, "\n");
            text = TagPattern.Replace(text, string.Empty);
            text = WebUtility.HtmlDecode(text);
            text = TrailingSpacePattern.Replace(text, "\n");
            text = MultiNewlinePattern.Replace(text, "\n\n");

            return text.Trim();
        }
    }
}
