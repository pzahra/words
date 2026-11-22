using System.Globalization;
using System.Text;
using Xunit;

namespace PatTech.Localization.Tests;

/// <summary>
/// The Rust crate's cases (words-ini/tests/fixtures/cases.txt), read by Core: each
/// row a fixture, a language, a call and the text it reads, which the crate's own
/// tests/shared.rs reads the same, so the two readers agree by test. A number among
/// the arguments goes in as a number, as an app would pass a count; the crate reads
/// a count from the text an argument shows. Format warns through
/// <see cref="Words.Logger"/>, hence the shared collection.
/// </summary>
[Collection("Words globals")]
public class WordsIniCasesTests {
	public static IEnumerable<object[]> Cases() {
		foreach (string line in File.ReadAllLines(Path.Combine(Fixtures, "cases.txt"))) {
			if (line.Length == 0 || line.StartsWith('#')) {
				continue;
			}
			string[] fields = line.Split(" | ", 6);
			if (fields.Length != 6) {
				throw new InvalidDataException($"a case is six fields: {line}");
			}
			yield return fields;
		}
	}

	[Theory]
	[MemberData(nameof(Cases))]
	public void CoreReadsEachCaseAsTheCrateDoes(string file, string language, string call, string key, string args, string expected) {
		IWords words = WordsBuilder.Create().Load(Path.Combine(Fixtures, file)).ToWords(language);
		string[] given = args == "-" ? [] : args.Split(';');
		object?[] positional = [.. given.Where(arg => !arg.Contains('=')).Select(Value)];
		Dictionary<string, object?> named = given.Where(arg => arg.Contains('=')).Select(arg => arg.Split('=', 2)).ToDictionary(pair => pair[0], pair => Value(pair[1]));

		string actual = call switch {
			"get" => words[key],
			"format" when named.Count > 0 => words.FormatByName(key, named, positional),
			"format" => words.Format(key, positional),
			"count" => words[key, decimal.Parse(given[0], CultureInfo.InvariantCulture)],
			_ => throw new InvalidDataException($"no call {call}"),
		};

		Assert.Equal(Unescape(expected), actual);
	}

	[Fact]
	public void TheCasesAreRead() {
		Assert.True(Cases().Count() > 50);
	}

	//a number as a number, as an app passes a count; anything else as text
	private static object? Value(string text)
		=> long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long whole) ? whole
		: decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number) ? number
		: text;

	//\n is a line break and \\ a backslash
	private static string Unescape(string text) {
		var out_ = new StringBuilder();
		for (int i = 0; i < text.Length; ++i) {
			if (text[i] == '\\' && i + 1 < text.Length) {
				out_.Append(text[++i] == 'n' ? '\n' : text[i]);
			}
			else {
				out_.Append(text[i]);
			}
		}
		return out_.ToString();
	}

	private static string Fixtures {
		get {
			string? at = AppContext.BaseDirectory;
			while (at is not null && !File.Exists(Path.Combine(at, "Words.slnx"))) {
				at = Path.GetDirectoryName(at);
			}
			return Path.Combine(at ?? throw new DirectoryNotFoundException("Words.slnx"), "words-ini", "tests", "fixtures");
		}
	}
}
