using PatTech.Localization;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels {

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
			Entries = original.Entries.ToDictionary(k => k.Key, v => new WordsEntry(v.Value));
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
			=> Entries[languageCode].Stale is not null;
	}

	public class WordsEntry : ViewModelBase {
		public string Value { get; set => ChangeProperty(ref field, value); }

		public string? Stale { get; set => ChangeProperty(ref field, value); }

		public string Context { get; set => ChangeProperty(ref field, value); }

		public string Comment { get; set => ChangeProperty(ref field, value); }

		public WordsEntry() {
			Value = "";
			Stale = "";
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

		public LanguageEntry(string code) {
			Code = code;
			NativeName = "MISSING NAME: " + code;
			EnglishName = "MISSING NAME: " + code;
		}

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

	public class DefaultWordsProvider(Dictionary<string, WordsKey> keys, string fileName) : IWordsProvider {
		public string this[string key] => throw new NotImplementedException();

		public bool ContainsKey(string key) => keys.ContainsKey(key);

		public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) {
			if (keys.TryGetValue(key, out var word)
				|| keys.TryGetValue($"{fileName}.{key}", out word)
			) {
				value = word.DefaultValue;
				return true;
			}
			else {
				value = null;
				return false;
			}
		}
	}

	public class LanguageWordsProvider : IWordsProvider {
		readonly Dictionary<string, WordsKey> keys;
		readonly string code;
		readonly string? family = null;
		readonly string fileName;

		public LanguageWordsProvider(Dictionary<string, WordsKey> keys, string code, string fileName) {
			this.keys = keys;
			this.code = code;
			if (code.Contains('-')) {
				family = code[..code.IndexOf('-')];
			}
			this.fileName = fileName;
		}
		public string this[string key] => throw new NotImplementedException();

		public bool ContainsKey(string key) {
			if (keys.TryGetValue(key, out var words)) {
				if (words.Entries.ContainsKey(code)) {
					return true;
				}
				if (family is not null && keys[key].Entries.ContainsKey(family)) {
					return true;
				}
			}

			// CHECK: why throw away the earlier checks against langauge code?
			if (keys.ContainsKey(key)) {
				return true;
			}
			else {
				return false;
			}
		}
		public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) {
			if (keys.TryGetValue(key, out var words)
				|| keys.TryGetValue($"{fileName}.{key}", out words)
			) {
				value = words.Entries[code].Value;
				if (value is "" && family is not null) {
					value = words.Entries[family].Value;
				}
				if (value is "") {
					value = words.DefaultValue;
				}
				return true;
			}
			else {
				value = null;
				return false;
			}
		}
	}

	public abstract class ViewModelSaveBase : ViewModelBase {
		public string TitleMarked => IsDirty ? Title + " *" : Title;
		public string Title { get; set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(TitleMarked)); } = "";
		public bool IsDirty { get; set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(TitleMarked)); }

		public abstract void Save();

		protected bool ChangeProperty<T>(
			[NotNullIfNotNull(nameof(newValue))] ref T field,
			T newValue,
			bool dirty = false,
			[CallerMemberName] string propertyName = ""
		) {
			if (ChangeProperty(ref field, newValue, propertyName)) {
				IsDirty |= dirty;
				return true;
			}
			return false;
		}
	}
}
