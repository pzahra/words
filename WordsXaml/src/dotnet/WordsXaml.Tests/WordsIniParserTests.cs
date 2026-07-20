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
public void Repeated_value_lines_concatenate_onto_the_same_key()
{
    const string ini =
@"[k]
value=one 
value=two 
value=three
";
    var entry = WordsIniParser.Parse(ini, "f.ini").Single(e => e.Key == "k");
    Assert.Equal("one two three", entry.DefaultValue);
}
    }
}
