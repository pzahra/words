using PatTech.Localization;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PatTech.Localization.Authoring {
	/// <summary>A language as a translation document names it: its code, and its name when the file's table has one.</summary>
	public sealed record TranslationLanguage(string Code, string? Name) {
		public override string ToString() => Name is { } name ? $"{name} ({Code})" : Code;
	}

	/// <summary>
	///     The request a translator fills (editor SPEC: Machine translation): one JSON
	///     document that explains itself, each key with its default and forms, its
	///     context, comment and parameters, the target's words where it has some, and
	///     a null for each slot to fill. A slot is named as in the file less the
	///     language: <c>value</c>, the plain value, or <c>value#few</c>, a form. The
	///     answer is the same document filled (<see cref="TranslationAnswer"/>).
	/// </summary>
	public static class TranslationRequest {
		/// <summary>What the document's <c>words</c> says it is; a reader refuses another.</summary>
		public const string Kind = "translation/1";

		/// <summary>The plain value's slot.</summary>
		public const string Plain = "value";

		/// <summary>The slot of <paramref name="form"/>: <c>value</c> for the plain value (empty), <c>value#few</c> for a form.</summary>
		public static string Slot(string form) => form == "" ? Plain : $"{Plain}#{form}";

		/// <summary>An entry in <paramref name="code"/> marked stale, which a request asks to translate again; a constant never.</summary>
		public static bool Stale(WordsKey key, string code) => !key.IsConstant && key.HasStaleValue(code);

		/// <summary>Whether the default has words to translate: a key with none asks nothing.</summary>
		public static bool HasWords(WordsKey key) => key.DefaultValue.Trim() != "" || key.Forms.HasWords();

		/// <summary>
		///     The slots <paramref name="key"/> has in <paramref name="code"/>: the plain
		///     value, and on a plural key each form the language counts by and does not
		///     leave optional (<see cref="MissingWords.Requires"/>).
		/// </summary>
		public static IReadOnlyList<string> Slots(WordsKey key, string code)
			=> [Plain, .. MissingWords.IsPlural(key) ? PluralRules.Categories(code).Where(category => MissingWords.Requires(code, category)).Select(Slot) : []];

		/// <summary>The slots a request asks for: every one of a stale entry, else those without words; none of a constant, which is not translated.</summary>
		public static IReadOnlyList<string> Asked(WordsKey key, string code) {
			if (key.IsConstant) {
				return [];
			}
			if (Stale(key, code)) {
				return Slots(key, code);
			}
			var words = Held(key.Entries.GetValueOrDefault(code));
			return [.. Slots(key, code).Where(slot => !words.TryGetValue(slot, out string? text) || text.Trim() == "")];
		}

		/// <summary>The default's slots with words, the plain value first, then its forms in CLDR's order.</summary>
		public static IReadOnlyDictionary<string, string> Held(WordsKey key) => Held(key.DefaultValue, key.Forms);

		/// <summary>An entry's slots with words, as <see cref="Held(WordsKey)"/>; none for no entry.</summary>
		public static IReadOnlyDictionary<string, string> Held(WordsEntry? entry) => entry is null ? new Dictionary<string, string>() : Held(entry.Value, entry.Forms);

		private static Dictionary<string, string> Held(string plain, IReadOnlyDictionary<string, string> forms) {
			Dictionary<string, string> words = [];
			if (plain != "") {
				words[Plain] = plain;
			}
			foreach (var (form, text) in forms.Written()) {
				words[Slot(form)] = text;
			}
			return words;
		}

		/// <summary>
		///     The request for <paramref name="keys"/>, in their order, each asking for
		///     <see cref="Asked"/>. A key without words in the default, or with nothing
		///     to ask, is left out.
		/// </summary>
		/// <param name="file">The file's name, for the translator to read.</param>
		/// <param name="source">The default's language.</param>
		/// <param name="target">The language asked for.</param>
		/// <param name="keys">The keys, by the file's own names.</param>
		/// <param name="brief">The user's own words to the translator, for the tone and the terms; none when empty.</param>
		/// <returns>The document, indented, its lines broken with <c>\n</c> and nothing after the last.</returns>
		public static string Write(string file, TranslationLanguage source, TranslationLanguage target, IEnumerable<WordsKey> keys, string brief = "") {
			var items = keys.Where(HasWords).Select(key => (Key: key, Asked: Asked(key, target.Code))).Where(item => item.Asked.Count != 0).ToList();
			var buffer = new ArrayBufferWriter<byte>();
			using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) {
				json.WriteStartObject();
				json.WriteString("words", Kind);
				json.WriteString("instructions", Instructions(source, target));
				if (brief != "") {
					json.WriteString("brief", brief);
				}
				json.WriteString("file", file);
				//the counts each slot reads, where a key has forms to read them for
				Language("source", source, items.SelectMany(item => Held(item.Key).Keys));
				Language("target", target, items.SelectMany(item => item.Asked));
				json.WriteStartArray("items");
				foreach (var (key, asked) in items) {
					json.WriteStartObject();
					json.WriteString("key", key.BlockKey);
					Slots("default", Held(key));
					if (key.Context != "") {
						json.WriteString("context", key.Context);
					}
					if (key.Comment != "") {
						json.WriteString("comment", key.Comment);
					}
					if (key.Parameters.Count != 0) {
						json.WriteStartObject("parameters");
						foreach (var parameter in key.Parameters) {
							json.WriteStartObject(parameter.Key);
							json.WriteString("type", parameter.DataType.ToString());
							json.WriteString("description", parameter.Description);
							json.WriteEndObject();
						}
						json.WriteEndObject();
					}
					var current = Held(key.Entries.GetValueOrDefault(target.Code));
					if (current.Count != 0) {
						Slots("current", current);
					}
					json.WriteStartObject("translation");
					foreach (string slot in asked) {
						json.WriteNull(slot);
					}
					json.WriteEndObject();
					json.WriteEndObject();
				}
				json.WriteEndArray();
				json.WriteEndObject();

				void Language(string name, TranslationLanguage language, IEnumerable<string> slots) {
					json.WriteStartObject(name);
					json.WriteString("code", language.Code);
					if (language.Name is { } named) {
						json.WriteString("name", named);
					}
					var read = slots.Distinct().ToList();
					if (read.Any(slot => slot != Plain)) {
						json.WriteStartObject("forms");
						foreach (string form in (string[])["", .. PluralRules.Names]) {
							string slot = Slot(form);
							string numbers = FormNumbers.Describe(language.Code, form == "" ? MissingWords.Plain : form, CultureInfo.InvariantCulture);
							if (read.Contains(slot) && numbers != "") {
								json.WriteString(slot, numbers);
							}
						}
						json.WriteEndObject();
					}
					json.WriteEndObject();
				}

				void Slots(string name, IReadOnlyDictionary<string, string> words) {
					json.WriteStartObject(name);
					foreach (var (slot, text) in words) {
						json.WriteString(slot, text);
					}
					json.WriteEndObject();
				}
			}
			return Encoding.UTF8.GetString(buffer.WrittenSpan);
		}

		//the brief every translator gets, in English as the field names are
		private static string Instructions(TranslationLanguage source, TranslationLanguage target)
			=> $"Translate each item's default from {source} into {target}. Write each translation in place of the null under "
			+ "the item's \"translation\", and change nothing else: \"value\" is the plain text, and a slot such as \"value#few\" "
			+ "is the form a plural takes for the counts \"target.forms\" gives it. Keep every {…} token, `code` span and link "
			+ "target exactly as the default has them, and keep its line breaks. \"context\" and \"comment\" say where the text "
			+ "shows, and \"parameters\" what each {…} token becomes. Where an item has a \"current\" translation, improve it "
			+ "rather than start over. Follow the \"brief\", if there is one. Return the whole document as JSON and nothing "
			+ "else, and leave a null you cannot fill.";
	}
}
