using System.Linq;
using WordsXaml.Ini;
using Xunit;

namespace WordsXaml.Tests
{
    public class WordsIniParserTests
    {
        // Mirrors the real helios-words.ini grammar: sections, continuations, refs, icons, locales.
        private const string Sample =
@"value=!(common)
value-en=English

[calibration.help.reset-hint]
value=Press the reset icon [icon:reset_circle] at any time to restart the calibration procedure.

[calibration.help.element-performance.body]
value=1: Position the probe on a flat block.\
2: Adjust the range.\
{>calibration.help.reset-hint}

[params.focal-law-base.capture-delay]
value=Capture Delay
value-en=Capture Delay
";

        [Fact]
        public void Parses_section_keys()
        {
            var entries = WordsIniParser.Parse(Sample, "helios-words.ini");
            Assert.Contains(entries, e => e.Key == "calibration.help.reset-hint");
            Assert.Contains(entries, e => e.Key == "params.focal-law-base.capture-delay");
        }

        [Fact]
        public void Captures_invariant_and_locale_values()
        {
            var entries = WordsIniParser.Parse(Sample, "f.ini");
            var capture = entries.Single(e => e.Key == "params.focal-law-base.capture-delay");
            Assert.Equal("Capture Delay", capture.DefaultValue);
            Assert.Equal("Capture Delay", capture.Values["en"]);
        }

        [Fact]
        public void Joins_backslash_continuations()
        {
            var entries = WordsIniParser.Parse(Sample, "f.ini");
            var body = entries.Single(e => e.Key == "calibration.help.element-performance.body");
            Assert.Contains("1: Position the probe", body.DefaultValue);
            Assert.Contains("2: Adjust the range.", body.DefaultValue);
            Assert.Contains("{>calibration.help.reset-hint}", body.DefaultValue);
            Assert.Contains("\n", body.DefaultValue);
        }

        [Fact]
        public void Dot_sections_inherit_the_last_fully_qualified_key()
        {
            const string ini =
@"[material]
[.metals]
value=METALS
[.composites]
value=COMPOSITES

[gate.mode]
value=Trigger
[.peak]
value=Max Peak
";
            var entries = WordsIniParser.Parse(ini, "evo-words.ini");

            Assert.Contains(entries, e => e.Key == "material.metals" && e.DefaultValue == "METALS");
            Assert.Contains(entries, e => e.Key == "material.composites"); // sibling still hangs off [material]
            Assert.Contains(entries, e => e.Key == "gate.mode.peak" && e.DefaultValue == "Max Peak");
            Assert.DoesNotContain(entries, e => e.Key.StartsWith("."));
        }

        [Fact]
        public void An_empty_header_names_no_key_and_pours_nothing_into_the_key_above()
        {
            const string ini =
@"[above]
value=kept
[]
value=lost
[.child]
value=orphan
";
            var entries = WordsIniParser.Parse(ini, "f.ini");

            // [] once matched no header, so its value overwrote above's and [.child] hung off it
            Assert.Equal("kept", entries.Single(e => e.Key == "above").DefaultValue);
            Assert.DoesNotContain(entries, e => e.Key == "above.child" || e.Key == "" || e.Key == ".child");
        }

        [Fact]
        public void Records_section_line_numbers_for_go_to_definition()
        {
            var entries = WordsIniParser.Parse(Sample, "f.ini");
            var hint = entries.Single(e => e.Key == "calibration.help.reset-hint");
            Assert.Equal(4, hint.LineNumber); // 1-based, matches editor gutter
        }

        [Fact]
public void Underscore_continuations_concatenate_without_a_newline()
{
    const string ini =
@"[k]
value=long value split_
 across two lines
";
    var entry = WordsIniParser.Parse(ini, "f.ini").Single(e => e.Key == "k");
    Assert.Equal("long value split across two lines", entry.DefaultValue);
    Assert.DoesNotContain("\n", entry.DefaultValue);
}

[Fact]
public void A_repeated_value_line_overwrites_as_the_runtime_does()
{
    // WordsParserToWordsProvider.Store: a later value for the same key and language wins (WB:KOVR).
    const string ini =
@"[k]
value=one
value=two
value=three
value-en=uno
value-en=dos\
tail
";
    var entry = WordsIniParser.Parse(ini, "f.ini").Single(e => e.Key == "k");
    Assert.Equal("three", entry.DefaultValue);
    Assert.Equal("dos\ntail", entry.Values["en"]); // the continuation follows the winning line
}

        [Fact]
        public void A_header_whose_name_is_no_key_is_skipped_with_its_fields_and_children()
        {
            const string ini =
@"[lang.c#]
value=C sharp\
[not.a.header]
[.child]
value=orphan
[a b]
value=spaced
[.child]
value=orphan too
[lang.c]
value=C
[.child]
value=kept
";
            var entries = WordsIniParser.Parse(ini, "f.ini");
            Assert.Equal(new[] { "lang.c", "lang.c.child" }, entries.Select(e => e.Key));
            Assert.Equal("C", entries[0].DefaultValue);
            Assert.Equal("kept", entries[1].DefaultValue);
        }

        [Fact]
        public void A_constant_has_no_children()
        {
            const string ini =
@"[$unit]
value=mm
[.child]
value=no
[.other]
value=no either
";
            var entries = WordsIniParser.Parse(ini, "f.ini");
            var unit = Assert.Single(entries);
            Assert.Equal("$unit", unit.Key);
            Assert.Equal("mm", unit.DefaultValue);
        }

        [Fact]
        public void A_dot_header_before_any_full_header_is_no_key()
        {
            const string ini =
@"value=!en
[.early]
value=resolves to '.early', which is no key name
[full]
[.child]
value=ok
";
            var entries = WordsIniParser.Parse(ini, "f.ini");
            Assert.Equal(new[] { "full", "full.child" }, entries.Select(e => e.Key));
        }

        [Fact]
        public void Headers_are_recognised_at_the_line_start_without_trimming()
        {
            const string ini =
@"[ok]
  [indented]
[ spaced ]
[tail] ; a comment after the header is fine
  value=an indented field is no field
";
            var entries = WordsIniParser.Parse(ini, "f.ini");
            Assert.Equal(new[] { "ok", "tail" }, entries.Select(e => e.Key));
            Assert.Null(entries[1].DefaultValue);
        }

        [Fact]
        public void A_block_named_twice_is_one_key_with_its_fields_merged()
        {
            const string ini =
@"[k]
value=default
[other]
[k]
value-de=Standard
";
            var entries = WordsIniParser.Parse(ini, "f.ini");
            var k = entries.Single(e => e.Key == "k");
            Assert.Equal(1, k.LineNumber);
            Assert.Equal("default", k.DefaultValue);
            Assert.Equal("Standard", k.Values["de"]);
        }

        [Fact]
        public void Continued_lines_are_never_headers_or_fields()
        {
            const string ini =
@"[k]
value=first\
[not.a.header]\
value=not a field_
;not a comment
[next]
value=n
";
            var entries = WordsIniParser.Parse(ini, "f.ini");
            Assert.Equal(new[] { "k", "next" }, entries.Select(e => e.Key));
            Assert.Equal("first\n[not.a.header]\nvalue=not a field;not a comment", entries[0].DefaultValue);
        }

        [Fact]
        public void Doubled_escapes_do_not_continue_and_unescape_to_one()
        {
            const string ini =
@"[path]
value=C:\\temp\\
[snake]
value=a__
[mixed]
value=it''s 50\\% and a_\
[next]
value=n
";
            var entries = WordsIniParser.Parse(ini, "f.ini");
            Assert.Equal(new[] { "path", "snake", "mixed", "next" }, entries.Select(e => e.Key));
            Assert.Equal(@"C:\temp\", entries[0].DefaultValue);
            Assert.Equal("a_", entries[1].DefaultValue);
            // '_\' reads as an escaped pair, so the line does not continue (the runtime's rxIsContinuedLine)
            Assert.Equal(@"it's 50\% and a_\", entries[2].DefaultValue);
        }

        [Fact]
        public void An_odd_run_of_escapes_still_continues()
        {
            const string ini =
@"[k]
value=a\\\
b___
c
";
            var entry = WordsIniParser.Parse(ini, "f.ini").Single();
            Assert.Equal("a\\\nb_c", entry.DefaultValue);
        }

        [Fact]
        public void Comments_are_semicolon_lines_and_a_hash_line_is_skipped_as_unrecognised()
        {
            const string ini =
@"; a comment
   ; an indented comment
[k]
# not a comment, but no field either
#value=no
value=yes
";
            var entry = WordsIniParser.Parse(ini, "f.ini").Single();
            Assert.Equal("yes", entry.DefaultValue);
            Assert.Single(entry.Values);
        }

        [Fact]
        public void Field_separators_and_spacing_follow_the_runtime()
        {
            const string ini =
                "[k]\n" +
                "value :   colon and spaces\n" +
                "value-en=  trimmed lead, kept tail  \n" +
                "Value=wrong case is no value field\n";
            var entry = WordsIniParser.Parse(ini, "f.ini").Single();
            Assert.Equal("colon and spaces", entry.DefaultValue);
            Assert.Equal("trimmed lead, kept tail  ", entry.Values["en"]);
            Assert.Equal(2, entry.Values.Count);
        }

        [Fact]
        public void Plural_forms_are_forms_of_the_key_not_locales()
        {
            const string ini =
@"[word]
value=Word
value#other=Words
value-it=Parola
value-it#other=Parole
value-mt#two=Kelmtejn
value-MT#Few=Kelmiet
value-ru#few=long_
er
value#one=refused: the plain value is that form
value#several=refused: no CLDR category
value#dual=refused\
with its continuation
";
            var entry = WordsIniParser.Parse(ini, "f.ini").Single();
            Assert.Equal(new[] { "", "it" }, entry.Values.Keys.OrderBy(k => k));
            Assert.DoesNotContain(entry.Values.Keys, k => k.Contains("#"));
            Assert.Equal("Words", entry.Forms[""]["other"]);
            Assert.Equal("Parole", entry.Forms["it"]["other"]);
            Assert.Equal("Kelmtejn", entry.Forms["mt"]["two"]);
            Assert.Equal("Kelmiet", entry.Forms["mt"]["few"]); // code and form cased as the runtime cases them
            Assert.Equal("longer", entry.Forms["ru"]["few"]);
            Assert.Equal(new[] { "two", "few", "other" }, entry.FormNames);
            Assert.Equal("Word", entry.DefaultValue);
        }

        [Fact]
        public void Language_codes_have_up_to_three_parts_cased_by_kind()
        {
            const string ini =
@"[k]
value-zh-Hans-CN=简体
value-sr-latn-rs=srpski
value-es-419=hola
value-ceb=kumusta
value-EN-gb=colour
";
            var entry = WordsIniParser.Parse(ini, "f.ini").Single();
            Assert.Equal("简体", entry.Values["zh-Hans-CN"]);
            Assert.Equal("srpski", entry.Values["sr-Latn-RS"]);
            Assert.Equal("hola", entry.Values["es-419"]);
            Assert.Equal("kumusta", entry.Values["ceb"]);
            Assert.Equal("colour", entry.Values["en-GB"]);
        }

        [Fact]
        public void A_field_whose_language_is_no_code_is_read_past_with_its_continuation()
        {
            const string ini =
@"[k]
value-english=no\
[still.continued]
value-en_GB=no either
value-zh-Hans-CN-x=too many parts
value=yes
";
            var entries = WordsIniParser.Parse(ini, "f.ini");
            var entry = Assert.Single(entries);
            Assert.Equal("yes", entry.DefaultValue);
            Assert.Single(entry.Values);
        }

        [Theory]
        [InlineData("menu.file-open", true)]
        [InlineData("é-ü.x_1", true)]
        [InlineData("$unit", true)]
        [InlineData("$unit.child", false)]
        [InlineData("a.$b", false)]
        [InlineData(".leading", false)]
        [InlineData("-dash", false)]
        [InlineData("a..b", false)]
        [InlineData("a b", false)]
        [InlineData("lang.c#", false)]
        [InlineData("", false)]
        public void IsKeyName_follows_the_runtime_grammar(string key, bool expected)
        {
            Assert.Equal(expected, WordsIniParser.IsKeyName(key));
        }
    }
}
