using PatTech.Localization;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels {

	public class LocalizationKey : ViewModelBase {
		private string _BlockKey;
		public string BlockKey {
			get => _BlockKey;
			set => ChangeProperty(ref _BlockKey, value);
		}

		private bool _IsConstant;
		public bool IsConstant {
			get => _IsConstant;
			set => ChangeProperty(ref _IsConstant, value);
		}

		private string _DefaultValue = "";
		public string DefaultValue {
			get => _DefaultValue;
			set => ChangeProperty(ref _DefaultValue, value);
		}

		private string _Context = "";
		public string Context {
			get => _Context;
			set => ChangeProperty(ref _Context, value);
		}

		private string _Comment = "";
		public string Comment {
			get => _Comment;
			set => ChangeProperty(ref _Comment, value);
		}

		private bool _NeedsReview = false;
		public bool NeedsReview {
			get => _NeedsReview;
			set => ChangeProperty(ref _NeedsReview, value);
		}

		public ObservableCollection<LocalizationParameter> Parameters { get; } = new();

		public Dictionary<string, LocalizationKeyLanguageData> LanguageData { get; } = new();

		public LocalizationKey(string blockKey) {
			_BlockKey = blockKey;
		}

		public LocalizationKey(LocalizationKey original) {
			_BlockKey = original._BlockKey;
			_IsConstant = original._IsConstant;
			_DefaultValue = original._DefaultValue;
			_Context = original._Context;
			_Comment = original._Comment;
			LanguageData = new Dictionary<string, LocalizationKeyLanguageData>();
			foreach (var kvp in original.LanguageData) {
				LocalizationKeyLanguageData languageData = new LocalizationKeyLanguageData(kvp.Value);
				LanguageData.Add(kvp.Key, languageData);
			}
		}

		public bool IsEmpty() {
			if(_IsConstant == false &&  _DefaultValue == "" && _Context == ""
					&& _Comment == "" && Parameters.Count == 0 && NeedsReview == false) {
				if (LanguageData.Values.All(localizationKeyLanguageData => localizationKeyLanguageData.IsEmpty())) {
					return true;
				}
			}
			return false;
		}

		public bool LanguageHasStaleValue(string languageCode) {
			return LanguageData[languageCode].StaleComment is not null;
		}
	}

	public class LocalizationKeyLanguageData : ViewModelBase {
		private string _Value = "";
		public string Value {
			get => _Value;
			set => ChangeProperty(ref _Value, value);
		}

		private string? _StaleComment;
		public string? StaleComment {
			get => _StaleComment;
			set => ChangeProperty(ref _StaleComment, value);
		}

		private string _LanguageContext = "";
		public string LanguageContext {
			get => _LanguageContext;
			set => ChangeProperty(ref _LanguageContext, value);
		}

		private string _LanguageComment = "";
		public string LanguageComment {
			get => _LanguageComment;
			set => ChangeProperty(ref _LanguageComment, value);
		}

		public LocalizationKeyLanguageData() { }

		public LocalizationKeyLanguageData(LocalizationKeyLanguageData original) {
			_Value = original._Value;
			_StaleComment = original._StaleComment;
			_LanguageContext = original._LanguageContext;
			_LanguageComment = original._LanguageComment;
		}

		public bool IsEmpty() {
			if (Value == "" && _StaleComment is null && _LanguageContext == "" && _LanguageComment == "") {
				return true;
			}
			return false;
		}

	}

	public class LocalizationLanguage : ViewModelBase {
		private string _Code;
		public string Code {
			get => _Code;
			set => ChangeProperty(ref _Code, value);
		}

		private string _NativeName = "";
		public string NativeName {
			get => _NativeName;
			set => ChangeProperty(ref _NativeName, value);
		}

		private string _EnglishName = "";
		public string EnglishName {
			get => _EnglishName;
			set => ChangeProperty(ref _EnglishName, value);
		}

		public LocalizationLanguage(string code, string nativeName) {
			_Code = code;
			_NativeName = nativeName;
			_EnglishName = _NativeName;
		}

		public LocalizationLanguage(string code) {
			_Code = code;
			_NativeName = "MISSING NAME: " + code;
			_EnglishName = "MISSING NAME: " + code;
		}

		public LocalizationLanguage(LocalizationLanguage other) {
			_Code = other.Code;
			_NativeName = other.NativeName;
			_EnglishName = other.EnglishName;
		}
	}

	public class LocalizationParameter : ViewModelBase {
		private string _Key = "";
		public string Key {
			get => _Key;
			set => ChangeProperty(ref _Key, value);
		}

		private string _Value = "";
		public string Value {
			get => _Value;
			set => ChangeProperty(ref _Value, value);
		}

		private LocalizationParameterType _DataType = LocalizationParameterType.String;
		public LocalizationParameterType DataType {
			get => _DataType;
			set => ChangeProperty(ref _DataType, value);
		}
		public LocalizationParameter() { }

		public LocalizationParameter(LocalizationParameter parameter) {
			Key = parameter.Key;
			Value = parameter.Value;
			DataType = parameter.DataType;
		}

		public object ToObject() => DataType.Parse(Key, Value);
	}

	public class LocalizationParameterType {
		public static readonly LocalizationParameterType[] All = new LocalizationParameterType[] {
			new("String", typeof(string), v => v),
			new("Integer", typeof(int), v => int.TryParse(v, out var result) ? result : null),
			new("Double", typeof(double), v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null),
			new("TimeSpan", typeof(TimeSpan), v => TimeSpan.TryParse(v, CultureInfo.InvariantCulture, out var result) ? result : null),
			new("DateTimeOffset", typeof(DateTimeOffset), v => DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result) ? result : null),
		};
		public static readonly LocalizationParameterType String = All[0];
		public static LocalizationParameterType Select(string name) => All.FirstOrDefault(t => t.Name == name) ?? String;

		public string Name { get; }
		public Type DataType { get; }

		private readonly Func<string, string, object> ParseCore;

		public LocalizationParameterType(string name, Type dataType, Func<string, object?> parse) {
			Name = name;
			DataType = dataType;
			ParseCore = (key, value) => parse(value)
				?? throw new FormatException($"Invalid value \"{value}\" for parameter {key} of type {name}");
		}

		public object Parse(string key, string value) => ParseCore(key, value);
	}

	public class LocalizationDefaultWordsProvider : IWordsProvider {
		readonly Dictionary<string, LocalizationKey> _localizationKeyDictionary;
		readonly string _fileName;

		public LocalizationDefaultWordsProvider(Dictionary<string, LocalizationKey> localizationKeyDictionary, string fileName) {
			_localizationKeyDictionary = localizationKeyDictionary;
			_fileName = fileName;
		}
		public string this[[DisallowNull] string key] => throw new NotImplementedException();

		public bool ContainsKey([DisallowNull] string key) {
			return _localizationKeyDictionary.ContainsKey(key);
		}
		public bool TryGetValue([DisallowNull] string key, [MaybeNullWhen(false)] out string value) {
			if (ContainsKey(key)) {
				value = _localizationKeyDictionary[key].DefaultValue;
				return true;
			}
			else if (ContainsKey($"{_fileName}.{key}")) {
				value = _localizationKeyDictionary[$"{_fileName}.{key}"].DefaultValue;
				return true;
			}
			else {
				value = null;
				return false;
			}
		}
	}

	public class LocalizationLanguageWordsProvider : IWordsProvider {
		readonly Dictionary<string, LocalizationKey> _localizationKeyDictionary;
		readonly string _primaryLanguageCode;
		readonly string? _secondaryLanguageCode = null;
		readonly string _fileName;

		public LocalizationLanguageWordsProvider(Dictionary<string, LocalizationKey> localizationKeyDictionary, string languageCode, string fileName) {
			_localizationKeyDictionary = localizationKeyDictionary;
			_primaryLanguageCode = languageCode;
			if (languageCode.Contains('-')) {
				_secondaryLanguageCode = languageCode[..languageCode.IndexOf('-')];
			}
			_fileName = fileName;
		}
		public string this[[DisallowNull] string key] => throw new NotImplementedException();

		public bool ContainsKey([DisallowNull] string key) {
			if (_localizationKeyDictionary.ContainsKey(key)
				&& _localizationKeyDictionary[key].LanguageData.ContainsKey(_primaryLanguageCode)) {
				return true;
			}
			else if (_localizationKeyDictionary.ContainsKey(key)
				&& _secondaryLanguageCode is not null && _localizationKeyDictionary[key].LanguageData.ContainsKey(_secondaryLanguageCode)) {
				return true;
			}
			else if (_localizationKeyDictionary.ContainsKey(key)) {
				return true;
			}
			else {
				return false;
			}
		}
		public bool TryGetValue([DisallowNull] string key, [MaybeNullWhen(false)] out string value) {
			if (ContainsKey(key)) {
				if (_localizationKeyDictionary[key].LanguageData[_primaryLanguageCode].Value != "") {
					value = _localizationKeyDictionary[key].LanguageData[_primaryLanguageCode].Value;
				}
				else if (_secondaryLanguageCode is not null && _localizationKeyDictionary[key].LanguageData[_secondaryLanguageCode].Value != "") {
					value = _localizationKeyDictionary[key].LanguageData[_secondaryLanguageCode].Value;
				}
				else {
					value = _localizationKeyDictionary[key].DefaultValue;
				}
				return true;
			}
			else if (ContainsKey($"{_fileName}.{key}")) {
				if (_localizationKeyDictionary[$"{_fileName}.{key}"].LanguageData[_primaryLanguageCode].Value != "") {
					value = _localizationKeyDictionary[$"{_fileName}.{key}"].LanguageData[_primaryLanguageCode].Value;
				}
				else if (_secondaryLanguageCode is not null
					&& _localizationKeyDictionary[$"{_fileName}.{key}"].LanguageData[_secondaryLanguageCode].Value != "") {
					value = _localizationKeyDictionary[$"{_fileName}.{key}"].LanguageData[_secondaryLanguageCode].Value;
				}
				else {
					value = _localizationKeyDictionary[$"{_fileName}.{key}"].DefaultValue;
				}
				return true;
			}
			else {
				value = null;
				return false;
			}
		}
	}

	public abstract class LocalizationViewModelSaveBase : ViewModelBase {

		public string Title {
			get {
				var title = "Wordsmith Editor";
				if (IsDirty) {
					title += " *";
				}
				return title;
			}
		}

		private bool _IsDirty;
		public bool IsDirty {
			get => _IsDirty;
			set {
				if (ChangeProperty(ref _IsDirty, value)) {
					AffectProperty(nameof(Title));
				}
			}
		}

		public abstract void Save();

		protected bool ChangeProperty<T>(
			[NotNullIfNotNull("newValue")] ref T field,
			T newValue,
			bool dirty = false,
			[DisallowNull, CallerMemberName] string propertyName = ""
		) {
			if (ChangeProperty(ref field, newValue, propertyName)) {
				IsDirty |= dirty; return true;
			}
			return false;
		}
	}
}
