using System.IO;
using System.Linq;
using WordsXaml.Ini;
using Xunit;

namespace WordsXaml.Tests
{
    public class WordsIndexTests
    {
        private static WordsIndex Build()
        {
            const string ini =
@"[calibration.help.reset-hint]
value=Press the reset icon [icon:reset_circle] to restart.

[calibration.help.element-performance.body]
value=Check the chart carefully before you continue with the next calibration step and confirm the result.

[params.focal-law-base.capture-delay]
value=Capture Delay
";
            return new WordsIndex(WordsIniParser.Parse(ini, "helios-words.ini"));
        }

        [Fact]
        public void CommittedPrefix_cuts_at_last_dot()
        {
            Assert.Equal("", WordsIndex.CommittedPrefix("cal"));
            Assert.Equal("params.", WordsIndex.CommittedPrefix("params.fo"));
            Assert.Equal("params.focal-law-base.", WordsIndex.CommittedPrefix("params.focal-law-base.cap"));
            // A reparsed completion sentinel in the partial segment must not affect the committed prefix.
            Assert.Equal("params.", WordsIndex.CommittedPrefix("params.foSENTINEL"));
        }

        [Fact]
        public void CompleteSegments_at_root_returns_distinct_top_level_branches()
        {
            var segs = Build().CompleteSegments("");
            var texts = segs.Select(s => s.InsertText).ToList();
            Assert.Contains("calibration.", texts);
            Assert.Contains("params.", texts);
            Assert.All(segs, s => Assert.True(s.IsBranch)); // no top-level leaf keys in this sample
            // Two keys share "calibration." -> one branch with childCount 2.
            Assert.Equal(2, segs.Single(s => s.InsertText == "calibration.").ChildCount);
        }

        [Fact]
        public void CompleteSegments_drills_into_a_level()
        {
            var segs = Build().CompleteSegments("calibration.");
            var texts = segs.Select(s => s.InsertText).ToList();
            Assert.Equal(new[] { "calibration.help." }, texts); // both keys collapse to one next segment
            Assert.Equal(2, segs[0].ChildCount);
        }

        [Fact]
        public void CompleteSegments_emits_leaves_with_preview_at_terminal_level()
        {
            var segs = Build().CompleteSegments("params.focal-law-base.");
            var leaf = Assert.Single(segs);
            Assert.False(leaf.IsBranch);
            Assert.Equal("params.focal-law-base.capture-delay", leaf.InsertText);
            Assert.Equal("Capture Delay", leaf.LeafPreview);
        }

        [Fact]
        public void CompleteSegments_uses_canonical_key_casing()
        {
            var segs = Build().CompleteSegments("PARAMS."); // user typed wrong case
            Assert.Equal("params.focal-law-base.", segs.Single().InsertText); // inserts canonical casing
        }

        [Fact]
        public void Match_is_prefix_first()
        {
            var index = Build();
            var keys = index.Match("calibration").ToList();
            Assert.All(keys, k => Assert.StartsWith("calibration", k.Key));
            Assert.Equal(2, keys.Count);
        }

        [Fact]
        public void Match_is_case_insensitive_substring()
        {
            var index = Build();
            Assert.Contains(index.Match("CAPTURE"), e => e.Key == "params.focal-law-base.capture-delay");
        }

        [Fact]
        public void RenderPreview_leaves_tokens_verbatim()
        {
            // Icon/cross-ref tokens are intentionally NOT expanded; they show as-is.
            var preview = Build().RenderPreview("calibration.help.reset-hint");
            Assert.Contains("[icon:reset_circle]", preview);
        }

        [Fact]
        public void RenderPreview_truncates_long_values_to_one_line()
        {
            var preview = Build().RenderPreview("calibration.help.element-performance.body", maxLength: 20);
            Assert.EndsWith("…", preview);
            Assert.True(preview.Length <= 21); // 20 chars + ellipsis
            Assert.DoesNotContain("\n", preview);
        }

        [Fact]
        public void Unknown_key_returns_null_preview()
        {
            Assert.Null(Build().RenderPreview("does.not.exist"));
        }

        [Fact]
        public void A_key_with_only_forms_previews_empty_but_is_known()
        {
            var index = new WordsIndex(WordsIniParser.Parse("[files]\nvalue#other=Files\n", "f.ini"));
            Assert.Equal("", index.RenderPreview("files"));
            Assert.Equal("#other", index.RenderForms("files"));
        }

        [Fact]
        public void A_key_declared_in_two_files_merges_per_language_and_the_later_file_wins()
        {
            var first = WordsIniParser.Parse("[k]\nvalue=first\nvalue-de=erste\nvalue#other=firsts\n", "a-words.ini");
            var second = WordsIniParser.Parse("[k]\nvalue=second\n", "b-words.ini");
            var index = new WordsIndex(first.Concat(second));
            Assert.True(index.TryGet("k", out var k));
            Assert.Equal("second", k.DefaultValue);
            Assert.Equal("erste", k.Values["de"]);
            Assert.Equal("firsts", k.Forms[""]["other"]);
            Assert.Equal("a-words.ini", k.FilePath); // the first declaration's location
        }

        [Fact]
        public void Preview_shows_the_runtime_unescaped_value()
        {
            var index = new WordsIndex(WordsIniParser.Parse("[k]\nvalue=it''s a__b\n", "f.ini"));
            Assert.Equal("it's a_b", index.RenderPreview("k"));
        }

        [Fact]
        public void QuickDoc_names_the_key_its_preview_forms_and_file()
        {
            var index = new WordsIndex(WordsIniParser.Parse(
                "[word]\nvalue=Word & co\nvalue#other=Words\nvalue-ru#few=Slova\n", Path.Combine("x", "app-words.ini")));
            var html = WordsQuickDoc.Html(index, "word");
            Assert.Equal("<b>word</b><br/>Word &amp; co<br/>forms: #few #other<br/><i>app-words.ini</i>", html);
            Assert.Null(WordsQuickDoc.Html(index, "nope"));
        }
    }
}
