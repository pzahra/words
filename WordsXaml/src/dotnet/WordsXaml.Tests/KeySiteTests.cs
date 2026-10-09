using WordsXaml.Sites;
using Xunit;

namespace WordsXaml.Tests
{
    public class KeySiteTests
    {
        [Theory]
        [InlineData("{l:Words main.title}", "main.title", 9)]
        [InlineData("{l:Words Key=main.title}", "main.title", 13)]
        [InlineData("{l:Words Key = main.title }", "main.title", 15)]
        [InlineData("{l:Words 'main.title'}", "main.title", 10)]
        [InlineData("{l:Words \"main.title\"}", "main.title", 10)]
        [InlineData("{l:Words Key='main.title'}", "main.title", 14)]
        [InlineData("{l:WordsExtension main.title}", "main.title", 18)]
        [InlineData("{Words $unit}", "$unit", 7)]
        [InlineData("{ l:Words   main.title  }", "main.title", 12)]
        public void Each_spelling_of_the_key_is_found(string markup, string key, int start)
        {
            var span = MarkupKeys.Parse(markup);
            Assert.NotNull(span);
            Assert.Equal(key, span.Key);
            Assert.Equal(start, span.Start);
            Assert.Equal(key, markup.Substring(span.Start, span.Length));
        }

        [Theory]
        [InlineData("{l:Words {Binding KeyName}}")]
        [InlineData("{l:Words}")]
        [InlineData("{Binding main.title}")]
        [InlineData("{l:Wordsmith main.title}")]
        [InlineData("not markup")]
        public void No_key_where_none_is_named(string markup)
        {
            Assert.Null(MarkupKeys.Parse(markup));
        }

        [Fact]
        public void A_named_key_wins_over_a_positional_one()
        {
            var span = MarkupKeys.Parse("{l:Words positional, Key=named}");
            Assert.Equal("named", span.Key);
        }

        [Theory]
        [InlineData("{l:Words }", 9)]
        [InlineData("{l:Words Key=}", 13)]
        [InlineData("{l:Words ''}", 10)]
        [InlineData("{l:Words ", 9)]
        public void An_empty_key_sits_where_it_is_due(string markup, int start)
        {
            var span = MarkupKeys.Parse(markup);
            Assert.Equal("", span.Key);
            Assert.Equal(start, span.Start);
        }

        [Theory]
        [InlineData("<TextBlock Text=\"{l:Words main.ti|}\"/>", "main.ti")]
        [InlineData("<TextBlock Text=\"{l:Words main.ti|\"/>", "main.ti")]
        [InlineData("<TextBlock Text=\"{l:Words |}\"/>", "")]
        [InlineData("<TextBlock Text=\"{l:Words Key=ma|in}\"/>", "main")]
        [InlineData("<TextBlock Text=\"{l:Words 'main.ti|}\"/>", "main.ti")]
        [InlineData("<TextBlock Text=\"{l:Words '|main'}\"/>", "main")]
        [InlineData("<TextBlock Text=\"{l:Words main.title|}\" Tag=\"{Binding X}\"/>", "main.title")]
        public void The_caret_finds_the_key_it_is_on(string marked, string key)
        {
            var caret = marked.IndexOf('|');
            var text = marked.Remove(caret, 1);
            var span = MarkupKeys.FindAt(text, caret);
            Assert.NotNull(span);
            Assert.Equal(key, span.Key);
        }

        [Theory]
        [InlineData("<TextBlock Text=\"{l:Words main.title}|\"/>")]
        [InlineData("<TextBlock Text=\"{l:Words 'main.title'|}\"/>")]
        [InlineData("<TextBlock Text=\"{l:Words| main.title}\"/>")]
        [InlineData("<TextBlock Text=\"{Binding ma|in}\"/>")]
        [InlineData("<TextBlock Text=\"{l:Words {Binding Ke|y}}\"/>")]
        [InlineData("<TextBlock Text=\"{l:Words main.title}\" Tag=\"x|\"/>")]
        public void The_caret_off_a_key_finds_none(string marked)
        {
            var caret = marked.IndexOf('|');
            var text = marked.Remove(caret, 1);
            Assert.Null(MarkupKeys.FindAt(text, caret));
        }

        [Theory]
        [InlineData("\"main.title\"", "main.title", 1)]
        [InlineData("@\"main.title\"", "main.title", 2)]
        [InlineData("\"\"\"main.title\"\"\"", "main.title", 3)]
        [InlineData("\"main.ti", "main.ti", 1)]
        [InlineData("\"\"", "", 1)]
        [InlineData("\"", "", 1)]
        public void A_string_literal_holds_its_content_as_the_key(string token, string key, int start)
        {
            var span = LiteralKeys.Parse(token);
            Assert.Equal(key, span.Key);
            Assert.Equal(start, span.Start);
        }

        [Theory]
        [InlineData("$\"main.{x}\"")]
        [InlineData("\"main\"u8")]
        [InlineData("'m'")]
        [InlineData("")]
        public void Other_literals_hold_no_key(string token)
        {
            Assert.Null(LiteralKeys.Parse(token));
        }
    }
}
