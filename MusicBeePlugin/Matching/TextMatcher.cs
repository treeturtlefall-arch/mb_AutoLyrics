using System;
using System.Text;
using System.Text.RegularExpressions;

namespace MusicBeePlugin.Matching
{
    public static class TextMatcher
    {
        private static readonly Regex BracketContentRegex = new Regex(@"[\(\[｛〔〈《「『【]\s*(feat|featuring|ft|コラボ|カバー)\b[^)\]｝〕〉》」』】]*[\)\]｝〕〉》」』】]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AnyBracketRegex = new Regex(@"[\(\[｛〔〈《「『】【』》〉〕｝\)\]]", RegexOptions.Compiled);
        private static readonly Regex BracketGroupRegex = new Regex(@"\([^\)]*\)|\[[^\]]*\]|\{[^}]*\}|（[^）]*）|〔[^〕]*〕|〈[^〉]*〉|《[^》]*》|「[^」]*」|『[^』]*』|【[^】]*】", RegexOptions.Compiled);

        public static string CleanSearchTitle(string trackTitle)
        {
            if (string.IsNullOrWhiteSpace(trackTitle))
            {
                return string.Empty;
            }

            var cleaned = BracketGroupRegex.Replace(trackTitle.Normalize(NormalizationForm.FormKC), " ");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            return string.IsNullOrEmpty(cleaned) ? trackTitle.Trim() : cleaned;
        }

        public static string StripBracketGroups(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            return Canonicalize(BracketGroupRegex.Replace(input.Normalize(NormalizationForm.FormKC), " "));
        }

        private static string Canonicalize(string text)
        {
            text = AnyBracketRegex.Replace(text, " ");
            text = Regex.Replace(text, @"[!""#$%&'\(\)=~\|<>`\{\}+\*;:?\-_\/\\\^\[\]@$,\.]", " ");
            text = Regex.Replace(text, @"\s+", " ");
            return text.Trim().ToLowerInvariant();
        }

        public static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var text = input.Normalize(NormalizationForm.FormKC);
            text = BracketContentRegex.Replace(text, " ");
            return Canonicalize(text);
        }

        public static bool IsSimilar(string left, string right, double threshold)
        {
            var l = Normalize(left);
            var r = Normalize(right);

            if (l.Length == 0 && r.Length == 0)
            {
                return true;
            }
            if (l.Length == 0 || r.Length == 0)
            {
                return false;
            }
            if (l == r)
            {
                return true;
            }

            if (Similarity(l, r) >= threshold)
            {
                return true;
            }

            var ls = StripBracketGroups(left);
            var rs = StripBracketGroups(right);
            if (ls.Length > 0 && rs.Length > 0)
            {
                return ls == rs || Similarity(ls, rs) >= threshold;
            }

            return false;
        }

        public static double Similarity(string left, string right)
        {
            if (left.Length == 0 && right.Length == 0) return 1.0;
            if (left.Length == 0 || right.Length == 0) return 0.0;

            var distance = Levenshtein(left, right);
            var maxLength = Math.Max(left.Length, right.Length);
            return 1.0 - (double)distance / maxLength;
        }

        public static int Levenshtein(string source, string target)
        {
            var prev = new int[target.Length + 1];
            var curr = new int[target.Length + 1];

            for (var j = 0; j <= target.Length; j++)
            {
                prev[j] = j;
            }

            for (var i = 1; i <= source.Length; i++)
            {
                curr[0] = i;
                for (var j = 1; j <= target.Length; j++)
                {
                    var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                var tmp = prev;
                prev = curr;
                curr = tmp;
            }

            return prev[target.Length];
        }
    }
}
