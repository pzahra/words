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
		}

		public bool IsEmpty()
			=> Value == ""
			&& Stale is null
			&& Context == ""
			&& Comment == "";
	}

	public class LanguageEntry : ViewModelBase {
		public string Code { get; set => ChangeProperty(ref field, value); }
		/// <summary>From Value</summary>
		public string NativeName { get; set => ChangeProperty(ref field, value); }
		/// <summary>From Comment</summary>
		public string EnglishName { get; set => ChangeProperty(ref field, value); }

		public LanguageEntry(string code, string nativeName) {
			Code = code;
			NativeName = nativeName;
			EnglishName = nativeName;
		}

		/// <summary>
		///     A placeholder for a language that has entries but no top-of-file
		///     label: shown as <c>!code</c> so it stays selectable, never written
		///     back as a label. <see cref="IsPlaceholder"/> recognizes it.
		/// </summary>
		public LanguageEntry(string code) {
			Code = code;
			NativeName = "!" + code;
			EnglishName = "!" + code;
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
	///     file's prefix, later-loaded files winning.
	/// </summary>
	public abstract class WordsProviderBase(IReadOnlyDictionary<string, WordsKey> keys, IEnumerable<string> fileNames) : IWordsProvider {
		private readonly string[] fileNames = [.. fileNames.Reverse()];

		public string this[string key] => throw new NotImplementedException();

		public bool ContainsKey(string key) => TryFind(key, out _);

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

		public abstract bool TryGetValue(string key, [MaybeNullWhen(false)] out string value);
	}

	public class DefaultWordsProvider(IReadOnlyDictionary<string, WordsKey> keys, IEnumerable<string> fileNames)
			: WordsProviderBase(keys, fileNames) {
		public override bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) {
			if (TryFind(key, out var word)) {
				value = word.DefaultValue;
				return true;
			}
			value = null;
			return false;
		}
	}

	public class LanguageWordsProvider(IReadOnlyDictionary<string, WordsKey> keys, string code, IEnumerable<string> fileNames)
			: WordsProviderBase(keys, fileNames) {
		private readonly string? family = code.Contains('-') ? code[..code.IndexOf('-')] : null;

		public override bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) {
			if (TryFind(key, out var word)) {
				value = word.Entries.GetValueOrDefault(code)?.Value ?? "";
				if (value is "" && family is not null) {
					value = word.Entries.GetValueOrDefault(family)?.Value ?? "";
				}
				if (value is "") {
					value = word.DefaultValue;
				}
				return true;
			}
			value = null;
			return false;
		}
	}
}
