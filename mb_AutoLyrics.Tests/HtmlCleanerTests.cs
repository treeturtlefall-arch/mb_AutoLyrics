using MusicBeePlugin.Common;
using Xunit;

namespace mb_AutoLyrics.Tests
{
    public class HtmlCleanerTests
    {
        [Fact]
        public void CleanToPlainText_RemovesRubyAndRt()
        {
            var html = "<ruby><rb>夢</rb><rp>(</rp><rt>ゆめ</rt><rp>)</rp></ruby>ならばどれほどよかったでしょう";
            var result = HtmlCleaner.CleanToPlainText(html, removeRuby: true);
            Assert.Equal("夢ならばどれほどよかったでしょう", result);
        }

        [Fact]
        public void CleanToPlainText_RemovesSpanRt_UtaTenStyle()
        {
            var html = "夢<span class=\"rt\">ゆめ</span>ならばどれほどよかったでしょう";
            var result = HtmlCleaner.CleanToPlainText(html, removeRuby: true);
            Assert.Equal("夢ならばどれほどよかったでしょう", result);
        }

        [Fact]
        public void CleanToPlainText_ReplacesBrWithNewlines()
        {
            var html = "行1<br>行2<br />行3<br/>行4";
            var result = HtmlCleaner.CleanToPlainText(html);
            Assert.Equal("行1\n行2\n行3\n行4", result);
        }

        [Fact]
        public void CleanToPlainText_DecodesHtmlEntities()
        {
            var html = "Rock &amp; Roll &lt;3&#39;s";
            var result = HtmlCleaner.CleanToPlainText(html);
            Assert.Equal("Rock & Roll <3's", result);
        }

        [Fact]
        public void CleanToPlainText_HandlesEmptyOrWhitespace()
        {
            Assert.Equal(string.Empty, HtmlCleaner.CleanToPlainText(null));
            Assert.Equal(string.Empty, HtmlCleaner.CleanToPlainText("   "));
        }
    }
}
