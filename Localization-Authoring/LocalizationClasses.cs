using PatTech.Localization;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

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

	public class WordsParameter : ViewModelBase {
		public string Key {
			get => field;
			set => ChangeProperty(ref field, value);
		}

		public string Value {
			get => field;
			set => ChangeProperty(ref field, value);
		}

		public WordsParameterType DataType {
			get => field;
			set => ChangeProperty(ref field, value);
		}
		public WordsParameter(string key, WordsParameterType dataType, string value) {
			Key = key;
			DataType = dataType;
			Value = value;
		}

		public WordsParameter(WordsParameter parameter) {
			Key = parameter.Key;
			Value = parameter.Value;
			DataType = parameter.DataType;
		}

		public object ToObject() => DataType.Parse(Key, Value);
	}

	public class WordsParameterType(string name, Type dataType, Func<string, object?> parse) {
		public static readonly WordsParameterType[] All = [
			new("String", typeof(string), v => v),
			new("Integer", typeof(int), v => int.TryParse(v, out var result) ? result : null),
			new("Double", typeof(double), v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null),
			new("TimeSpan", typeof(TimeSpan), v => TimeSpan.TryParse(v, CultureInfo.InvariantCulture, out var result) ? result : null),
			new("DateTimeOffset", typeof(DateTimeOffset), v => DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result) ? result : null),
		];
		public static readonly WordsParameterType String = All[0];
		public static WordsParameterType Select(string name) => All.FirstOrDefault(t => t.Name == name) ?? String;

		public string Name { get; } = name;
		public Type DataType { get; } = dataType;

		private readonly Func<string, string, object> ParseCore = (key, value) => parse(value)
				?? throw new FormatException($"Invalid value \"{value}\" for parameter {key} of type {name}");

		public object Parse(string key, string value) => ParseCore(key, value);
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
