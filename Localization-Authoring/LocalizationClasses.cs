using PatTech.Localization;
using PatTech.Utils;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PatTech.Localization.Authoring {

	public class WordsKey : ViewModelBase {
		public string BlockKey { get; set => ChangeProperty(ref field, value); }

		public bool IsConstant { get; set => ChangeProperty(ref field, value); }

		public string DefaultValue { get; set => ChangeProperty(ref field, value); }

		public string Context { get; set => ChangeProperty(ref field, value); }

		public string Comment { get; set => ChangeProperty(ref field, value); }

		public bool NeedsReview { get; set => ChangeProperty(ref field, value); }

		public ObservableCollection<WordsParameter> Parameters { get; } = [];

		public Dictionary<string, WordsEntry> Entries { get; } = [];

		/// <summary>
		///     The default's plural forms by CLDR category, <c>value#other</c>; the plain
		///     value is the <c>one</c> form, implied (runtime SPEC: Plural forms). Only
		///     CLDR's categories are written, and an empty form is none.
		/// </summary>
		public Dictionary<string, string> Forms { get; } = [];

		public WordsKey(string blockKey) {
			BlockKey = blockKey;
			DefaultValue = "";
			Context = "";
			Comment = "";
		}

		public WordsKey(WordsKey original) {
			BlockKey = original.BlockKey;
			IsConstant = original.IsConstant;
			DefaultValue = original.DefaultValue;
			Context = original.Context;
			Comment = original.Comment;
			NeedsReview = original.NeedsReview;
			Entries = original.Entries.ToDictionary(k => k.Key, v => new WordsEntry(v.Value));
			Forms = new(original.Forms);
			foreach (WordsParameter parameter in original.Parameters) {
				Parameters.Add(new WordsParameter(parameter));
			}
		}

		public bool IsEmpty()
			=> IsConstant == false
			&& DefaultValue == ""
			&& Context == ""
			&& Comment == ""
			&& Parameters.Count == 0
			&& NeedsReview == false
			&& !Forms.HasWords()
			&& Entries.Values.All(entry => entry.IsEmpty());

		public bool HasStaleValue(string languageCode)
			=> Entries.TryGetValue(languageCode, out var entry) && entry.Stale is not null;

		/// <summary>
		///     The translations a change to the default leaves stale: every language
		///     with words of its own and no stale mark yet. A mark already there
		///     stays, since its words and its date say how long the translation has
		///     gone unreviewed and why. An empty translation is missing, not stale.
		/// </summary>
		public IEnumerable<string> TranslationsToStale()
			=> Entries.Where(pair => pair.Value.Stale is null && (pair.Value.Value != "" || pair.Value.Forms.HasWords())).Select(pair => pair.Key);

		/// <summary>The stale mark Wordsmith and <c>words</c> write when the default changes: the time, in the invariant culture.</summary>
		public static string StaleStamp(DateTimeOffset now) => now.ToString(CultureInfo.InvariantCulture);

		/// <summary>
		///     True when a regional variant of <paramref name="languageCode"/>
		///     (<c>en-GB</c> for <c>en</c>) carries a value of its own, so what the
		///     family renders is overridden somewhere below it.
		/// </summary>
		public bool HasRegionalOverride(string languageCode) {
			string prefix = languageCode + "-";
			foreach (var (code, entry) in Entries) {
				if (code.StartsWith(prefix, StringComparison.Ordinal) && entry.Value != "") {
					return true;
				}
			}
			return false;
		}
	}

	public class WordsEntry : ViewModelBase {
		public string Value { get; set => ChangeProperty(ref field, value); }

		public string? Stale { get; set => ChangeProperty(ref field, value); }

		public string Context { get; set => ChangeProperty(ref field, value); }

		public string Comment { get; set => ChangeProperty(ref field, value); }

		/// <summary>The language's plural forms by CLDR category, <c>value-xx#few</c>, as <see cref="WordsKey.Forms"/> holds the default's.</summary>
		public Dictionary<string, string> Forms { get; } = [];

		public WordsEntry() {
			Value = "";
			Stale = null;
			Context = "";
			Comment = "";
		}

		public WordsEntry(WordsEntry original) {
			Value = original.Value;
			Stale = original.Stale;
			Context = original.Context;
			Comment = original.Comment;
			Forms = new(original.Forms);
		}

		public bool IsEmpty()
			=> Value == ""
			&& Stale is null
			&& Context == ""
			&& Comment == ""
			&& !Forms.HasWords();
	}

	/// <summary>Plural forms as the model holds them: category → text, an empty text being no form.</summary>
	public static class PluralFormsExtensions {
		/// <summary>True when any form has words.</summary>
		public static bool HasWords(this IReadOnlyDictionary<string, string> forms) => forms.Values.Any(text => text != "");

		/// <summary>The forms with words, in CLDR's order; a category CLDR does not have is left out.</summary>
		public static IEnumerable<KeyValuePair<string, string>> Written(this IReadOnlyDictionary<string, string> forms) {
			foreach (string form in PluralRules.Names) {
				if (forms.TryGetValue(form, out string? text) && text != "") {
					yield return new(form, text);
				}
			}
		}
	}

	public class LanguageEntry : ViewModelBase {
		public string Code { get; set => ChangeProperty(ref field, value); }
		/// <summary>From Value</summary>
		public string NativeName { get; set => Rename(ref field, value); }
		/// <summary>From Comment: the name in the default's language, empty when none was given (and none is written).</summary>
		public string EnglishName { get; set => Rename(ref field, value); } = "";
		/// <summary>The name to show: <see cref="EnglishName"/>, or <see cref="NativeName"/> without one.</summary>
		public string DisplayName => EnglishName != "" ? EnglishName : NativeName;

		private void Rename(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string property = "") {
			if (ChangeProperty(ref field, value, property)) {
				AffectProperty(nameof(DisplayName));
			}
		}

		public LanguageEntry(string code, string nativeName) {
			Code = code;
			NativeName = nativeName;
		}

		/// <summary>
		///     A placeholder for a language that has entries but no top-of-file
		///     label: shown as <c>!code</c> so it stays selectable, never written
		///     back as a label. <see cref="IsPlaceholder"/> recognizes it.
		/// </summary>
		public LanguageEntry(string code) {
			Code = code;
			NativeName = "!" + code;
		}

		public bool IsPlaceholder => NativeName == $"!{Code}";

		public LanguageEntry(LanguageEntry other) {
			Code = other.Code;
			NativeName = other.NativeName;
			EnglishName = other.EnglishName;
		}
	}

	/// <summary>
	///     A parameter's definition, the programmer's (editor SPEC: Parameters → In the
	///     file): <c>param-x=type:Description</c>, what the code passes for <c>{x}</c> and
	///     what it is, for the translator. A value to try it with is the editor's
	///     session's, never the document's.
	/// </summary>
	public class WordsParameter : ViewModelBase {
		/// <summary>The parameter as the text names it: <c>0</c> for <c>{0}</c>, <c>Name</c> for <c>{Name}</c>.</summary>
		public string Key {
			get => field;
			set => ChangeProperty(ref field, value);
		}

		/// <summary>What it is, for the translator.</summary>
		public string Description {
			get => field;
			set => ChangeProperty(ref field, value);
		}

		/// <summary>What the code passes.</summary>
		public WordsParameterType DataType {
			get => field;
			set => ChangeProperty(ref field, value);
		}

		public WordsParameter(string key, WordsParameterType dataType, string description) {
			Key = key;
			DataType = dataType;
			Description = description;
		}

		public WordsParameter(WordsParameter parameter) {
			Key = parameter.Key;
			Description = parameter.Description;
			DataType = parameter.DataType;
		}

		/// <summary>The field's text as the writer writes it (<see cref="WordsParameterType.Join"/>).</summary>
		public string FieldText => WordsParameterType.Join(DataType, Description);
	}

	/// <summary>
	///     What the code passes for a parameter (editor SPEC: Parameters → Types): one of
	///     six, named in any case, each read from an input in the invariant culture, as
	///     the file's own numbers are. <c>enum</c> carries a key prefix, its members the
	///     keys directly under it. 1.3.0's names read as the short ones, and are written
	///     short.
	/// </summary>
	public sealed record WordsParameterType {
		/// <summary>Free text.</summary>
		public static WordsParameterType Str { get; } = new("str");
		/// <summary>A whole number, an <see cref="int"/>.</summary>
		public static WordsParameterType Int { get; } = new("int");
		/// <summary>A number with a fraction, a <see cref="double"/>.</summary>
		public static WordsParameterType Real { get; } = new("real");
		/// <summary>A span, a <see cref="TimeSpan"/>: <c>1:30:00</c>.</summary>
		public static WordsParameterType Time { get; } = new("time");
		/// <summary>A moment, a <see cref="DateTimeOffset"/>: <c>2026-10-10 09:30</c>, UTC unless it says otherwise.</summary>
		public static WordsParameterType Date { get; } = new("date");

		/// <summary>The five no prefix completes, in the order a list offers them.</summary>
		public static IReadOnlyList<WordsParameterType> Plain { get; } = [Str, Int, Real, Time, Date];

		/// <summary>One of the keys directly under <paramref name="prefix"/>: <c>enum(enums.brew)</c>.</summary>
		public static WordsParameterType Enum(string prefix) => new("enum", prefix);

		//1.3.0's names, which a file it wrote still holds
		private static readonly Dictionary<string, WordsParameterType> legacy = new(StringComparer.OrdinalIgnoreCase) {
			["String"] = Str, ["Integer"] = Int, ["Double"] = Real, ["TimeSpan"] = Time, ["DateTimeOffset"] = Date,
		};
		private static readonly Regex rxEnum = new(@"^enum\((?<prefix>[^()]*)\)\z", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);

		private WordsParameterType(string name, string? prefix = null) {
			Name = name;
			Prefix = prefix;
		}

		/// <summary>The type's word, short and lower case: <c>str</c>, <c>enum</c>.</summary>
		public string Name { get; }
		/// <summary>An <c>enum</c>'s key prefix, as written; <see langword="null"/> for the others.</summary>
		public string? Prefix { get; }

		/// <summary>The type as the file writes it: <c>int</c>, <c>enum(enums.brew)</c>.</summary>
		public override string ToString() => Prefix is null ? Name : $"{Name}({Prefix})";

		/// <summary>
		///     The type <paramref name="text"/> names, in any case: one of the six, its
		///     <c>enum</c> with a prefix that is a key's name, or one of 1.3.0's names.
		/// </summary>
		public static bool TryParse(string text, [NotNullWhen(true)] out WordsParameterType? type) {
			type = Plain.FirstOrDefault(plain => string.Equals(plain.Name, text, StringComparison.OrdinalIgnoreCase))
				?? legacy.GetValueOrDefault(text)
				?? (rxEnum.Match(text) is { Success: true } match && WordsParser.IsKeyName(match.Groups["prefix"].Value) ? Enum(match.Groups["prefix"].Value) : null);
			return type is not null;
		}

		/// <summary>
		///     A <c>param-x</c> field's text read: the words before the first <c>:</c> are
		///     the type only where they name one, so <c>the file: its full path</c> is a
		///     <c>str</c>'s description, whole, and so is a text with no <c>:</c>.
		/// </summary>
		public static (WordsParameterType Type, string Description) Split(string text) {
			int colon = text.IndexOf(':');
			return colon >= 0 && TryParse(text[..colon], out var type) ? (type, text[(colon + 1)..]) : (Str, text);
		}

		/// <summary>
		///     A <c>param-x</c> field's text as the writer writes it: the type before a
		///     <c>:</c> wherever it is not <c>str</c>, and for a <c>str</c> only where the
		///     description's own first words would read as a type.
		/// </summary>
		public static string Join(WordsParameterType type, string description)
			=> type == Str && Split(description) == (Str, description) ? description : $"{type}:{description}";

		/// <summary>
		///     What the code would pass, read from <paramref name="input"/> in the
		///     invariant culture: the text, the number, the span or the moment; for an
		///     <c>enum</c>, the member the input names under the prefix, described in
		///     <paramref name="words"/> as an app's <c>Describe()</c> shows it.
		/// </summary>
		/// <param name="name">The parameter's name, for the complaint.</param>
		/// <param name="input">What was typed.</param>
		/// <param name="words">The words an <c>enum</c>'s member is described in.</param>
		/// <exception cref="FormatException">The input is none of what the type reads.</exception>
		public object Read(string name, string input, IWords words) {
			object? value = Name switch {
				"int" => int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int whole) ? whole : null,
				"real" => double.TryParse(input, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double real) ? real : null,
				"time" => TimeSpan.TryParse(input, CultureInfo.InvariantCulture, out TimeSpan span) ? span : null,
				"date" => DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset moment) ? moment : null,
				"enum" => WordsParser.IsKeySegment(input) ? Describable.OfKey($"{Prefix}.{input}").Describe(null, words) : null,
				_ => input,
			};
			return value ?? throw new FormatException(Name switch {
				"int" => $"{{{name}}}: \"{input}\" is no whole number, such as 42",
				"real" => $"{{{name}}}: \"{input}\" is no number, such as 1.5",
				"time" => $"{{{name}}}: \"{input}\" is no span of time, such as 1:30:00",
				"date" => $"{{{name}}}: \"{input}\" is no moment, such as 2026-10-10 09:30",
				_ => $"{{{name}}}: \"{input}\" is no member under {Prefix}",
			});
		}
	}

	/// <summary>
	///     Resolves a key against every loaded file the way a host app stacking
	///     dictionaries would: an exact (already-prefixed) key hits directly; a bare
	///     reference like <c>{&gt;group.key}</c> or <c>{$constant}</c> probes each
	///     file's prefix, later-loaded files winning. A plural form, <c>key#few</c>,
	///     is there when the key has it at the first of the language, its family and
	///     the default that has any of its words, the way the runtime flattens forms.
	/// </summary>
	public abstract class WordsProviderBase(IReadOnlyDictionary<string, WordsKey> keys, IEnumerable<string> fileNames) : IWordsProvider {
		private readonly string[] fileNames = [.. fileNames.Reverse()];

		public string this[string key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException(key);

		public bool ContainsKey(string key) => TryGetValue(key, out _);

		protected bool TryFind(string key, [MaybeNullWhen(false)] out WordsKey word) {
			if (keys.TryGetValue(key, out word)) {
				return true;
			}
			foreach (var fileName in fileNames) {
				if (keys.TryGetValue($"{fileName}.{key}", out word)) {
					return true;
				}
			}
			return false;
		}

		public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) {
			if (TryFind(key, out var word)) {
				value = Value(word);
				return true;
			}
			int mark = key.LastIndexOf('#');
			if (mark > 0 && TryFind(key[..mark], out word) && Form(word, key[(mark + 1)..]) is { Length: > 0 } form) {
				value = form;
				return true;
			}
			value = null;
			return false;
		}

		/// <summary>What <paramref name="word"/> reads as.</summary>
		protected abstract string Value(WordsKey word);

		/// <summary><paramref name="word"/>'s <paramref name="form"/>, or empty when it has none.</summary>
		protected abstract string Form(WordsKey word, string form);
	}

	public class DefaultWordsProvider(IReadOnlyDictionary<string, WordsKey> keys, IEnumerable<string> fileNames)
			: WordsProviderBase(keys, fileNames) {
		protected override string Value(WordsKey word) => word.DefaultValue;

		protected override string Form(WordsKey word, string form) => word.Forms.GetValueOrDefault(form, "");
	}

	public class LanguageWordsProvider(IReadOnlyDictionary<string, WordsKey> keys, string code, IEnumerable<string> fileNames)
			: WordsProviderBase(keys, fileNames) {
		//the code, then each shorter one it falls back to, as the runtime flattens them
		private readonly string[] chain = LanguageCode.TryParse(code, out var parsed) ? [.. parsed.Chain.Select(level => level.ToString())] : [code];

		//the language's, else the first shorter code's, else the default's
		protected override string Value(WordsKey word) {
			foreach (string level in chain) {
				if (word.Entries.GetValueOrDefault(level)?.Value is { Length: > 0 } value) {
					return value;
				}
			}
			return word.DefaultValue;
		}

		//the forms of the first level with any of the key's words
		protected override string Form(WordsKey word, string form) {
			foreach (string level in chain) {
				if (word.Entries.GetValueOrDefault(level) is { } entry && (entry.Value != "" || entry.Forms.HasWords())) {
					return entry.Forms.GetValueOrDefault(form, "");
				}
			}
			return word.Forms.GetValueOrDefault(form, "");
		}
	}
}
