using PatTech.Localization;
using PatTech.Utils;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     One field of a key, named as the file names it: <c>value</c>,
	///     <c>value-fr</c>, <c>value-mt#few</c>, <c>context-fr</c>, <c>stale-fr</c>,
	///     <c>param-count</c>. The language is normalized and the form lowercased as
	///     the parser does, so a name compares with what a file declares.
	/// </summary>
	/// <param name="Type">The field without its language: <c>value</c>, <c>context</c>, <c>comment</c>, <c>stale</c> or <c>param</c>.</param>
	/// <param name="Language">The language code, the parameter's name for <c>param</c>, or empty for the default.</param>
	/// <param name="Form">The plural category of a <c>value</c> field, or empty for the plain value.</param>
	public readonly record struct WordsField(string Type, string Language, string Form) {
		//the parser's pair grammar, without the text
		private static readonly Regex rxName = new(@"^(?<type>\w+)(-(?<lang>\w+(?:-\w+)?))?(#(?<form>\w+))?$", RegexOptions.ExplicitCapture);

		/// <summary>The field types a key's block holds.</summary>
		public static IReadOnlyList<string> Types { get; } = ["value", "context", "comment", "stale", "param"];

		/// <summary>Reads a field's name; <paramref name="problem"/> says what is wrong with one that is no field.</summary>
		public static bool TryParse(string name, out WordsField field, [NotNullWhen(false)] out string? problem) {
			field = default;
			if (!rxName.TryMatch(name, out var match)) {
				problem = $"'{name}' is no field: write value, value-fr, value-mt#few, context-fr, stale-fr or param-name";
				return false;
			}
			string type = match.Groups["type"].Value;
			string language = WordsParser.NormalizeLanguageCasing(match.Groups["lang"].Value);
			string form = match.Groups["form"].Value.ToLowerInvariant();
			if (!Types.Contains(type)) {
				problem = $"'{name}': a key has no {type} field ({string.Join(", ", Types)})";
				return false;
			}
			if (form != "" && type != "value") {
				problem = $"'{name}': only a value has plural forms";
				return false;
			}
			if (form != "" && !PluralRules.Names.Contains(form)) {
				problem = $"'{name}': {form} is no plural category ({string.Join(", ", PluralRules.Names)})";
				return false;
			}
			if (type == "param" && language == "") {
				problem = $"'{name}': a parameter is named, param-name";
				return false;
			}
			field = new(type, language, form);
			problem = null;
			return true;
		}

		/// <summary>The name as the file writes it.</summary>
		public override string ToString()
			=> Type + (Language == "" ? "" : "-" + Language) + (Form == "" ? "" : "#" + Form);

		/// <summary>
		///     The field's text in <paramref name="key"/>, or <see langword="null"/>
		///     when it is not there. An empty text is none, except a stale mark, which
		///     is there with or without words; the default's stale mark keeps no words,
		///     and a parameter reads as written, <c>Type:value</c>.
		/// </summary>
		public string? Read(WordsKey key) {
			string name = Language;
			WordsEntry? entry = name == "" ? null : key.Entries.GetValueOrDefault(name);
			string? text = (Type, name == "") switch {
				("value", true) => Form == "" ? key.DefaultValue : key.Forms.GetValueOrDefault(Form),
				("value", false) => Form == "" ? entry?.Value : entry?.Forms.GetValueOrDefault(Form),
				("context", true) => key.Context,
				("context", false) => entry?.Context,
				("comment", true) => key.Comment,
				("comment", false) => entry?.Comment,
				("stale", true) => key.NeedsReview ? "" : null,
				("stale", false) => entry?.Stale,
				_ => key.Parameters.FirstOrDefault(parameter => parameter.Key == name) is { } parameter ? $"{parameter.DataType.Name}:{parameter.Value}" : null,
			};
			return text == "" && Type != "stale" ? null : text;
		}

		/// <summary>
		///     Sets the field's text in <paramref name="key"/> as reading the file back
		///     would, or clears it for <see langword="null"/>: the default's stale mark
		///     keeps no words, and a parameter without a <c>Type:</c> is a string.
		/// </summary>
		public void Write(WordsKey key, string? text) {
			if (Type == "param") {
				string name = Language;
				var existing = key.Parameters.FirstOrDefault(parameter => parameter.Key == name);
				if (existing is not null) {
					key.Parameters.Remove(existing);
				}
				if (text is not null) {
					string[] parts = text.Split(':', count: 2);
					key.Parameters.Add(parts.Length > 1
						? new WordsParameter(Language, WordsParameterType.Select(parts[0]), parts[1])
						: new WordsParameter(Language, WordsParameterType.String, parts[0]));
				}
				return;
			}
			if (Language == "") {
				switch (Type) {
					case "value" when Form == "":
						key.DefaultValue = text ?? "";
						break;
					case "value":
						SetForm(key.Forms, text);
						break;
					case "context":
						key.Context = text ?? "";
						break;
					case "comment":
						key.Comment = text ?? "";
						break;
					case "stale":
						key.NeedsReview = text is not null;
						break;
				}
				return;
			}
			if (!key.Entries.TryGetValue(Language, out var entry)) {
				key.Entries[Language] = entry = new WordsEntry();
			}
			switch (Type) {
				case "value" when Form == "":
					entry.Value = text ?? "";
					break;
				case "value":
					SetForm(entry.Forms, text);
					break;
				case "context":
					entry.Context = text ?? "";
					break;
				case "comment":
					entry.Comment = text ?? "";
					break;
				case "stale":
					entry.Stale = text;
					break;
			}
		}

		private void SetForm(Dictionary<string, string> forms, string? text) {
			if (text is null) {
				forms.Remove(Form);
			}
			else {
				forms[Form] = text;
			}
		}

		/// <summary>Every field <paramref name="key"/> holds, with its text as <see cref="Read"/> reads it.</summary>
		public static IEnumerable<(WordsField Field, string Text)> All(WordsKey key) {
			foreach (var field in Of(key)) {
				if (field.Read(key) is { } text) {
					yield return (field, text);
				}
			}
		}

		//every field the key could hold a text in
		private static IEnumerable<WordsField> Of(WordsKey key) {
			foreach (string type in (string[])["value", "context", "comment", "stale"]) {
				yield return new(type, "", "");
			}
			foreach (string form in key.Forms.Keys) {
				yield return new("value", "", form);
			}
			foreach (var parameter in key.Parameters) {
				yield return new("param", parameter.Key, "");
			}
			foreach (var (language, entry) in key.Entries) {
				foreach (string type in (string[])["value", "context", "comment", "stale"]) {
					yield return new(type, language, "");
				}
				foreach (string form in entry.Forms.Keys) {
					yield return new("value", language, form);
				}
			}
		}
	}
}
