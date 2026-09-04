using MusicBeePlugin.Matching;
using Xunit;

namespace mb_AutoLyrics.Tests
{
    public class TextMatcherTests
    {
        [Theory]
        [InlineData("夜に駆ける (Single Ver.)", "夜に駆ける")]
        [InlineData("Lemon [2024 Remaster]", "Lemon")]
        [InlineData("群青【通常盤】", "群青")]
        [InlineData("炎 (feat. LiSA)", "炎")]
        public void CleanSearchTitle_RemovesBrackets(string input, string expected)
        {
            var result = TextMatcher.CleanSearchTitle(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Lemon", "Lemon", true)]
        [InlineData("LEMON", "lemon", true)]
        [InlineData("夜に駆ける", "夜に駆ける (Single Ver.)", true)]
        [InlineData("Lemon", "Remon", false)]
        [InlineData("永遠の微笑み", "永遠の詩", false)]
        public void IsSimilar_MatchesExpected(string left, string right, bool expected)
        {
            var result = TextMatcher.IsSimilar(left, right, 0.85);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Similarity_IdenticalStrings_ReturnsOne()
        {
            Assert.Equal(1.0, TextMatcher.Similarity("米津玄師", "米津玄師"));
        }

        [Fact]
        public void Similarity_EmptyStrings_ReturnsOne()
        {
            Assert.Equal(1.0, TextMatcher.Similarity("", ""));
        }

        [Fact]
        public void Similarity_CompletelyDifferent_ReturnsLow()
        {
            var sim = TextMatcher.Similarity("米津玄師", "バナナフリッターズ");
            Assert.True(sim < 0.3);
        }
    }
}
