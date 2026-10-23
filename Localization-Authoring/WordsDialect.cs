using System.Text;
using System.Text.RegularExpressions;
using PatTech.Text.Ini;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     A <c>words.ini</c> as the ini driver reads and edits it (the editor's SPEC: Ini files
	///     through the driver, The dialects): the dotted dialect with the rules Core's parser reads
	///     by. Section names compare exactly (<c>[A]</c> and <c>[a]</c> are two keys), a field's name as Words
	///     names it (<c>value-FR</c> is <c>value-fr</c>); <c>=</c> or <c>:</c>; <c>\</c> and
	///     <c>_</c> continuations whose marker takes the character after it, <c>\\</c>,
	///     <c>__</c> and <c>''</c> read as one; no quotes; <c>;</c> comments on lines of their
	///     own; no <c>[]</c>, its lines read past; no header or field indented, anything after a
	///     header's <c>]</c> read past, and the spaces ending a value its own. A field is written
	///     as <see cref="IniWriter"/> writes it, folded and escaped; a field and a key new to the
	///     file go where the writer would put them (the command line's SPEC: Surgical edits); its
	///     chains are cut by <see cref="Text.Ini.GroupCuts"/>; and an edit is checked by reading the
	///     file back through Core: every key's fields, the language table, the settings references
	///     and the comments in order.
	/// </summary>
	public sealed class WordsDialect : IniDottedDialect {
		//ahead of Instance, which is made with them
		private static readonly IniRules WordsRules = WordsValues.Rules with {
			Names = StringComparer.Ordinal,
			KeyNames = WordsFieldNames.Instance,
		};

		/// <summary>The one dialect a <c>words.ini</c> is read in.</summary>
		public static WordsDialect Instance { get; } = new();

		//a reading of the text the last edit left, which is the next edit's before
		private WordsReading.Cached? last;

		private WordsDialect() : base(WordsRules, null) {
		}

		/// <inheritdoc/>
		protected override bool IsKey(string key) => WordsValues.IsKey(key);

		/// <inheritdoc/>
		protected override string Unescape(string text) => WordsValues.Unescape(text);

		/// <inheritdoc/>
		protected override string Escape(string text) => WordsValues.Escape(text);

		/// <summary>A field as the writer writes it, folded and escaped.</summary>
		protected override IReadOnlyList<string>? WriteField(string key, string value) => WordsValues.Pair(key, value);

		/// <summary>
		///     Where a field new to a key goes: where the writer puts it, when the key's fields stand
		///     in the writer's order (<see cref="Rank"/>), ahead of the first that ranks after it and
		///     of the comment block right above that one; else, in a block written in no order,
		///     after the last of its fields, or right under its last header when it has none. The
		///     language table's fields go as the plain dialect puts them.
		/// </summary>
		protected override IniPlacement PlaceKey(IniDocument document, string section, string key) {
			ArgumentNullException.ThrowIfNull(document);
			if (section.Length == 0) {
				return base.PlaceKey(document, section, key);
			}
			IReadOnlyList<IniElement> all = document.Elements;
			List<IniField> own = [.. all.OfType<IniField>().Where(field => field.Section == section)];
			if (own.Count == 0) {
				return new IniPlacement(all.OfType<IniHeader>().Last(header => header.Name == section).Line + 1);
			}
			//the writer's order is Core's: its entries and parameters as the key read; a block of
			//nothing but empty fields reads as a group, no key, and has neither
			List<string> languages = [], parameters = [];
			if (Read(document.Text).Words.WordKeys.TryGetValue(section, out var found)) {
				languages = [.. found.Entries.Keys];
				parameters = [.. found.Parameters.Select(parameter => parameter.Key)];
			}
			var ranks = own.Select(field => Rank(Field(field.Key), languages, parameters)).ToList();
			bool ordered = ranks.Zip(ranks.Skip(1)).All(pair => pair.First.CompareTo(pair.Second) <= 0);
			var rank = Rank(Field(key), languages, parameters);
			int later = ordered ? ranks.FindIndex(other => other.CompareTo(rank) > 0) : -1;
			if (later < 0) {
				return new IniPlacement(own.Max(field => field.Line + field.Count));
			}
			int at = own[later].Line;
			if (all.OfType<IniComment>().FirstOrDefault(comment => comment.Line + comment.Count == at) is { } above) {
				at = above.Line;
			}
			return new IniPlacement(at);
		}

		/// <summary>
		///     Where a key new to the file goes: beside its nearest sibling (<see cref="IniDottedDialect.Follows"/>),
		///     spaced as the writer spaces blocks in a file spaced so. The writer writes a blank line
		///     after each block with a value, so a blank line after the block it follows is that
		///     block's own: the new key goes past it, and has one of its own when it has a value. A
		///     block with a value and no blank line after it is a file written without them, and the
		///     new key gets none. In a file with no blocks it goes under the language table, after
		///     its blank line or with one.
		/// </summary>
		protected override IniPlacement PlaceSection(IniDocument document, string section, IReadOnlyList<KeyValuePair<string, string>> pairs) {
			ArgumentNullException.ThrowIfNull(document);
			ArgumentNullException.ThrowIfNull(pairs);
			if (section.Length == 0) {
				return base.PlaceSection(document, section, pairs);
			}
			bool valued = pairs.Any(pair => KeyNames.Equals(pair.Key, "value") && pair.Value != "");
			(int end, IniHeader? follows) = Follows(document, section);
			int count = document.LineCount;
			if (follows is null) {
				bool ends = count != 0 && document.IsBlank(count - 1);
				return new IniPlacement(count, BlankBefore: count != 0 && !ends, BlankAfter: ends && valued);
			}
			bool blank = end + 1 < count && document.IsBlank(end + 1);
			bool spaced = blank || document.Get(follows.Name, "value") is not { Length: > 0 };
			int at = blank ? end + 2 : end + 1;
			return new IniPlacement(at, BlankAfter: spaced && valued && (at == count || !document.IsBlank(at)));
		}

		/// <summary>
		///     The file read back through Core as the edit means it: every key it did not touch as
		///     it read, every key it touched as its pairs read, the language table and the settings
		///     references likewise, and the comments in order, as they were unless the edit's own
		///     were comments.
		/// </summary>
		protected override IEnumerable<IniDifference> CheckMore(IniChange change) {
			ArgumentNullException.ThrowIfNull(change);
			WordsReading before = Read(change.Before.Text), after = Read(change.After.Text);
			last = new(change.After.Text, after);
			HashSet<string> touched = Touched(change);
			var wanted = Flatten(before.Words.WordKeys);
			foreach (string section in touched.Where(section => section.Length > 0)) {
				wanted.Remove(section);
				if (Flatten(WordsReading.Of(Render(change, section)).Words.WordKeys).TryGetValue(section, out var fields)) {
					wanted[section] = fields;
				}
			}
			var got = Flatten(after.Words.WordKeys);
			List<IniDifference> found = [];
			foreach (string key in wanted.Keys.Concat(got.Keys).Distinct()) {
				if (!got.TryGetValue(key, out var have)) {
					found.Add(new(key, null, key, null));
				}
				else if (!wanted.TryGetValue(key, out var want)) {
					found.Add(new(key, null, null, key));
				}
				else {
					found.AddRange(want.Keys.Concat(have.Keys).Distinct()
						.Where(field => want.GetValueOrDefault(field) != have.GetValueOrDefault(field))
						.Select(field => new IniDifference(key, field, want.GetValueOrDefault(field), have.GetValueOrDefault(field))));
				}
			}
			WordsParserToLocalizationProvider table = touched.Contains("") ? WordsReading.Of(Render(change, "")).Words : before.Words;
			if (!SameTable(table, after.Words)) {
				found.Add(new("", "the language table", null, null));
			}
			if (table.DefaultLanguage != after.Words.DefaultLanguage || table.Settings != after.Words.Settings
				|| !table.LanguageSettings.OrderBy(pair => pair.Key).SequenceEqual(after.Words.LanguageSettings.OrderBy(pair => pair.Key))) {
				found.Add(new("", "the file's settings", null, null));
			}
			List<string> drafted = Comments(change.Before.Elements), left = Comments(change.After.Elements);
			IReadOnlyList<string> comments = drafted.SequenceEqual(left) ? before.Comments : left;
			if (!comments.SequenceEqual(after.Comments)) {
				found.Add(new("", "a comment", null, null));
			}
			return found;
		}

		//the reading of a text, the last edit's after taken again as this one's before
		private WordsReading Read(string text) => last is { } cached && cached.Text == text ? cached.Reading : WordsReading.Of(text);

		//the sections whose pairs the edit changes
		private HashSet<string> Touched(IniChange change) {
			HashSet<string> touched = new(StringComparer.Ordinal);
			foreach (string section in change.Before.Sections.Concat(change.ExpectedSections).Distinct(StringComparer.Ordinal)) {
				IReadOnlyList<string> was = change.Before.Keys(section), now = change.ExpectedKeys(section);
				if (was.Count != now.Count || was.Any(key => change.Before.Get(section, key) != change.Expected(section, key))) {
					touched.Add(section);
				}
			}
			return touched;
		}

		//a section's pairs as the edit means them, written as Save writes them, for Core to read
		private static string Render(IniChange change, string section) {
			var text = new StringBuilder();
			if (section.Length > 0) {
				text.Append('[').Append(section).Append("]\n");
			}
			foreach (string key in change.ExpectedKeys(section)) {
				foreach (string line in WordsValues.Pair(key, change.Expected(section, key)!)) {
					text.Append(line).Append('\n');
				}
			}
			return text.ToString();
		}

		private static bool SameTable(WordsParserToLocalizationProvider was, WordsParserToLocalizationProvider now)
			=> was.DeclaredLanguages.SequenceEqual(now.DeclaredLanguages)
				&& was.DeclaredLanguages.All(code => was.KnownLanguages[code].NativeName == now.KnownLanguages[code].NativeName
					&& was.KnownLanguages[code].EnglishName == now.KnownLanguages[code].EnglishName);

		//each comment line's text, as Core reads it: after its ;
		private static List<string> Comments(IReadOnlyList<IniElement> elements)
			=> [.. elements.OfType<IniComment>().SelectMany(block => block.Lines).Select(line => line.TrimStart()[1..])];

		//key → field → text, for every key that is not a bare group
		private static Dictionary<string, Dictionary<string, string>> Flatten(IReadOnlyDictionary<string, WordsKey> keys) {
			Dictionary<string, Dictionary<string, string>> flat = [];
			foreach (var (name, key) in keys) {
				if (!key.IsEmpty()) {
					flat[name] = WordsField.All(key).ToDictionary(pair => pair.Field.ToString(), pair => pair.Text);
				}
			}
			return flat;
		}

		//a field's name as Words reads it, a type Words has no field of kept as written
		private static WordsField Field(string name) {
			if (WordsField.TryParse(name, out var field, out _)) {
				return field;
			}
			int form = name.IndexOf('#');
			string head = form < 0 ? name : name[..form];
			int dash = head.IndexOf('-');
			return new WordsField(dash < 0 ? head : head[..dash], dash < 0 ? "" : head[(dash + 1)..], form < 0 ? "" : name[(form + 1)..].ToLowerInvariant());
		}

		//CLDR's categories, in the order the writer writes a key's forms
		private static readonly List<string> categories = [.. PluralRules.Names];

		/// <summary>
		///     A field's place as the writer writes a block (<see cref="IniWriter"/>): the context,
		///     the comment, the value and its forms in CLDR's order, the parameters in the key's
		///     order, the default's stale mark; then each language in the order the key's entries
		///     came in, its value, its forms, its stale mark, its context and its comment. A
		///     language or a parameter the key does not have yet comes after the ones it has.
		/// </summary>
		private static (int Group, int Index, int Place) Rank(WordsField field, List<string> languages, List<string> parameters) {
			int form = field.Form == "" ? 0 : 1 + categories.IndexOf(field.Form);
			if (field.Type == "param") {
				int index = parameters.IndexOf(field.Language);
				return (3, index < 0 ? parameters.Count : index, 0);
			}
			if (field.Language == "") {
				return field.Type switch {
					"context" => (0, 0, 0),
					"comment" => (1, 0, 0),
					"value" => (2, 0, form),
					_ => (4, 0, 0),
				};
			}
			int language = languages.IndexOf(field.Language);
			return (5, language < 0 ? languages.Count : language, field.Type switch {
				"value" => form,
				"stale" => 10,
				"context" => 11,
				_ => 12,
			});
		}
	}

	/// <summary>
	///     A settings file as the ini driver edits it (the editor's SPEC: Ini files through the
	///     driver, The dialects): plain sections, names without case, with the words dialect's
	///     value rules.
	/// </summary>
	internal sealed class SettingsDialect : IniDialect {
		public static SettingsDialect Instance { get; } = new();

		private SettingsDialect() : base(WordsValues.Rules with {
			Names = StringComparer.OrdinalIgnoreCase,
			KeyNames = StringComparer.OrdinalIgnoreCase,
		}) {
		}

		protected override bool IsKey(string key) => WordsValues.IsKey(key);

		protected override string Unescape(string text) => WordsValues.Unescape(text);

		protected override string Escape(string text) => WordsValues.Escape(text);

		protected override IReadOnlyList<string>? WriteField(string key, string value) => WordsValues.Pair(key, value);
	}

	/// <summary>How Core reads a field and Save writes one: the rules both dialects share.</summary>
	internal static class WordsValues {
		//the parser's pair grammar, before its separator
		private static readonly Regex rxKey = new(@"^\w+(-\w+(?:-\w+)*)?(#\w+)?\z", RegexOptions.CultureInvariant);
		//a doubled marker, or a doubled quote, read as one
		private static readonly Regex rxUnescape = new(@"([\\_'])\1", RegexOptions.CultureInvariant);
		private static readonly Regex rxEscape = new(@"[\\_']", RegexOptions.CultureInvariant);

		public static IniRules Rules { get; } = new() {
			Separators = IniSeparators.Either,
			Comments = IniCommentMarkers.Semicolon,
			Continuations = IniContinuations.Both,
			Escapes = IniEscapes.TakesNext,
			EmptyHeader = false,
			Indents = false,
			HeaderTails = true,
			TrailingSpaces = true,
			Encodings = [new UTF8Encoding(false)],
			ByteOrderMarks = IniByteOrderMarks.Kept,
		};

		public static bool IsKey(string key) => rxKey.IsMatch(key);

		public static string Unescape(string text) => rxUnescape.Replace(text, "$1");

		public static string Escape(string text) => rxEscape.Replace(text, match => match.Value + match.Value);

		//a field's lines as the writer writes it, each without its break
		public static IReadOnlyList<string> Pair(string key, string value) {
			var writer = new StringWriter { NewLine = "\n" };
			using var ini = new IniWriter(writer);
			ini.WritePair(key, value);
			string written = writer.ToString();
			return written[..^1].Split('\n');
		}
	}

	/// <summary>Field names compared as Words reads them: by the field each names, a name that is none as written.</summary>
	internal sealed class WordsFieldNames : StringComparer {
		public static WordsFieldNames Instance { get; } = new();

		public override int Compare(string? x, string? y) => string.CompareOrdinal(Canonical(x), Canonical(y));

		public override bool Equals(string? x, string? y) => string.Equals(Canonical(x), Canonical(y), StringComparison.Ordinal);

		public override int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(Canonical(obj)!);

		private static string? Canonical(string? name) => name is not null && WordsField.TryParse(name, out var field, out _) ? field.ToString() : name;
	}

	/// <summary>A <c>words.ini</c> read through Core: its model, its comments in order, and its keys in the order their blocks first stand.</summary>
	internal sealed class WordsReading : IWordsParserConsumer {
		private readonly HashSet<string> seen = [];

		public WordsParserToLocalizationProvider Words { get; } = new();
		public List<string> Comments { get; } = [];
		public List<string> Order { get; } = [];

		public static WordsReading Of(string text) {
			var reading = new WordsReading();
			new WordsParser(reading).Load(new StringReader(text));
			return reading;
		}

		public void VisitBlock(string baseKey, string key) {
			string name = key.StartsWith('.') ? baseKey + key : key;
			if (seen.Add(name)) {
				Order.Add(name);
			}
			((IWordsParserConsumer)Words).VisitBlock(baseKey, key);
		}

		public void VisitFieldDeclaration(FieldKey key, string text) => Words.VisitFieldDeclaration(key, text);

		public void VisitFieldContinuation(FieldKey key, string value) => Words.VisitFieldContinuation(key, value);

		public void VisitComment(string text) {
			Comments.Add(text);
			Words.VisitComment(text);
		}

		public void VisitBadLanguage(string blockKey, string name) => ((IWordsParserConsumer)Words).VisitBadLanguage(blockKey, name);

		/// <summary>A reading kept with the text it read.</summary>
		internal sealed record Cached(string Text, WordsReading Reading);
	}
}
