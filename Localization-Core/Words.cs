using PatTech.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PatTech.Localization {
	using PairObj = KeyValuePair<string, object?>;

	/// <summary>
	/// A read-only view of the loaded words for one selected language.
	/// Lookups return fully rendered text: <c>{$constant}</c> and <c>{&gt;key}</c>
	/// references are expanded before the string reaches you.
	/// </summary>
	public interface IWords {
		/// <summary>
		/// The flattened key/value store backing this view. Values read directly
		/// from the provider are raw: references are not yet expanded.
		/// </summary>
		IWordsProvider Provider { get; }

		/// <summary>
		/// Returns the rendered value of <paramref name="key"/>.
		/// A missing key does not throw; it renders as the placeholder <c>#key#</c>
		/// and a warning is sent to <see cref="Words.Logger"/>.
		/// </summary>
		/// <param name="key">The key to look up, e.g. <c>"group.key"</c> or <c>"$constant"</c>.</param>
		[Localized]
		string this[string key] { get; }

		/// <summary>
		/// Returns <paramref name="key"/>'s own form for <paramref name="count"/>, rendered
		/// like any value: the plural form CLDR's rule for <see cref="UICulture"/> picks
		/// (SPEC: Plural forms), its <c>other</c> form when it has none for that count,
		/// and its plain value when it has neither — so <c>Words.Known["word", 2]</c> is
		/// "Words" where <c>Words.Known["word"]</c> is "Word". A missing key renders as
		/// <c>#key#</c>, as for the indexer.
		/// </summary>
		/// <param name="key">The key to look up.</param>
		/// <param name="count">The count to pick the form by; a fractional one picks <c>other</c>.</param>
		[Localized]
		string this[string key, decimal count] => Words.RenderCount(this, key, count);

		/// <summary>
		/// The language this dictionary speaks, which picks a count's plural form. A
		/// dictionary that does not say speaks the thread's UI culture.
		/// </summary>
		CultureInfo UICulture => CultureInfo.CurrentUICulture;

		/// <summary>
		/// Checks whether <paramref name="key"/> exists in the underlying <see cref="Provider"/>.
		/// </summary>
		/// <param name="key">The key to look up.</param>
		/// <returns><see langword="true"/> if the key is present; otherwise <see langword="false"/>.</returns>
		bool ContainsKey(string key);
		/// <summary>
		/// Retrieves and renders the value of <paramref name="key"/> if it exists.
		/// </summary>
		/// <param name="key">The key to look up.</param>
		/// <param name="value">The rendered value, or <see langword="null"/> if the key is not present.</param>
		/// <returns><see langword="true"/> if the key was found; otherwise <see langword="false"/>.</returns>
		bool TryGetValue(string key, [MaybeNullWhen(false), Localized] out string value);

		/// <summary>
		/// Applies this dictionary's cultures to the current thread and to the
		/// process-wide defaults: the UI culture is the selected language, the formatting
		/// culture the one it was built with — the same, unless the builder was told
		/// <see cref="WordsBuilder.UseSystemNumbers"/>. Called automatically when
		/// assigning <see cref="Words.Known"/>.
		/// </summary>
		void SetCulture();
	}

	/// <summary>
	/// This is Words. It gives you words.
	/// Home of the process-wide dictionary (<see cref="Known"/>), the reference-rendering
	/// engine (<see cref="RenderKey(IWordsProvider, string, object[])"/>), and the
	/// <c>String.Format</c>-style helpers, including named-parameter formatting.
	/// </summary>
	public static partial class Words {
		private static IWords _Known = new CulturedWords(WordsProvider.Empty(), CultureInfo.InvariantCulture);
		private static readonly Regex rxFormatTag = new(
				@"\{[\s-[\r\n]]*(?<1>(?=[_a-zA-Z])\w+)[\s-[\r\n]]*(:[\s-[\r\n]]*(?<2>[^\r\n}]*(?<!\s))[\s-[\r\n]]*)?\}",
				RegexOptions.Compiled | RegexOptions.ExplicitCapture);
		private static readonly Regex rxUnescape = new(
				@"(?<1>[\\'""{])\1|\{[$>](?<2>[^}]+)\}",
				RegexOptions.Compiled | RegexOptions.ExplicitCapture);
		//{0#word} or {Count#word}; a {{ pair is matched only to be left for string.Format
		private static readonly Regex rxSelector = new(
				@"\{\{|\{(?<1>\d+|(?=[_a-zA-Z])\w+)#(?<2>[^{}#\s]+)\}",
				RegexOptions.Compiled | RegexOptions.ExplicitCapture);

		/// <summary>
		/// The process-wide dictionary, installed once at startup — usually by
		/// <see cref="WordsBuilder.Digest(string)"/>, which builds and assigns it in one
		/// call. Reads and writes are volatile, so the swap is safe from any thread.
		/// Assigning also calls <see cref="IWords.SetCulture"/> on the new value and, in
		/// live mode (<see cref="Live"/>), refreshes everything that <see cref="Watch"/>ed.
		/// Starts as an empty, invariant-culture dictionary, so every lookup renders as
		/// <c>#key#</c> until real words are loaded.
		/// </summary>
		/// <exception cref="ArgumentNullException">The value assigned is <see langword="null"/>.</exception>
		[DisallowNull, NotNull]
		public static IWords Known {
			get => Volatile.Read(ref _Known);
			set {
				ArgumentNullException.ThrowIfNull(value);
				Volatile.Write(ref _Known, value);
				value.SetCulture();
				if (Live is not null) {
					RefreshWatchers();
				}
			}
		}
		/// <summary>
		/// The culture the process started in — the system's regional format, captured the
		/// first time Words is touched, before any <see cref="Known"/> assignment could move
		/// the thread cultures. <see cref="WordsBuilder.UseSystemNumbers"/> formats with it.
		/// </summary>
		public static CultureInfo SystemCulture { get; } = CultureInfo.CurrentCulture;
		/// <summary>
		/// Receives warnings about missing keys, unknown constants, circular references
		/// and absent format fields. Defaults to a logger that discards everything;
		/// assign your own to hear about your typos. Assign once at startup; it is never
		/// null (assigning null throws) — use <see cref="ITakeException.Dummy"/> to silence.
		/// </summary>
		/// <exception cref="ArgumentNullException">The value assigned is <see langword="null"/>.</exception>
		public static ITakeException Logger {
			get => logger;
			set => logger = value ?? throw new ArgumentNullException(nameof(value));
		}
		private static ITakeException logger = ITakeException.Dummy;

		/// <summary>
		/// Shorthand for <see cref="WordsBuilder.Create(ITakeException?)"/> with no logger.
		/// </summary>
		public static WordsBuilder Builder() => WordsBuilder.Create();

		/// <summary>
		/// <see cref="RenderKey(IWordsProvider, string, object[])"/> in this dictionary's
		/// language, so <paramref name="args"/> also select plural forms (<c>{0#word}</c>)
		/// as <see cref="Format(IWords, IFormatProvider?, string, object?[])"/> does.
		/// </summary>
		/// <param name="words">The dictionary to resolve keys against.</param>
		/// <param name="key">The key to look up.</param>
		/// <param name="args">Optional arguments for the selectors and <see cref="string.Format(string, object[])"/>; <see langword="null"/> or empty skips both.</param>
		[return: NotNull, Localized]
		public static string RenderKey(
				[DisallowNull] this IWords words,
				[DisallowNull] string key,
				[AllowNull] object[] args = null) {
			ArgumentNullException.ThrowIfNull(words);
			return args?.Length > 0 ? words.Format(key, args) : RenderKey(words.Provider, key);
		}
		/// <summary>
		/// <see cref="RenderText(IWordsProvider, string, string, object[])"/> in this
		/// dictionary's language, so <paramref name="args"/> also select plural forms.
		/// </summary>
		/// <param name="words">The dictionary to resolve references against.</param>
		/// <param name="text">The template text to render.</param>
		/// <param name="baseKey">Resolves relative references and selectors: <c>{&gt;.sub}</c> becomes <c>baseKey.sub</c>.</param>
		/// <param name="args">Optional arguments for the selectors and <see cref="string.Format(string, object[])"/>; <see langword="null"/> or empty skips both.</param>
		[return: NotNull, Localized]
		public static string RenderText(
				[DisallowNull] this IWords words,
				[DisallowNull] string text,
				[AllowNull] string baseKey = null,
				[AllowNull] object[] args = null) {
			ArgumentNullException.ThrowIfNull(words);
			if (!(args?.Length > 0)) {
				return RenderText(words.Provider, text, baseKey);
			}
			var template = RenderText(words.Provider, text, baseKey);
			return string.Format(SelectForms(words, template, baseKey ?? "", Positional(args)), args);
		}

		/// <summary>
		/// Looks up <paramref name="key"/> and expands every <c>{$constant}</c> and
		/// <c>{&gt;key}</c> reference it contains, recursively. A missing key or constant
		/// renders as <c>#key#</c>; a circular reference renders as <c># ∞ #</c>;
		/// both are reported to <see cref="Logger"/> rather than thrown.
		/// </summary>
		/// <param name="wordsProvider">The dictionary to resolve keys against.</param>
		/// <param name="key">The key to look up. Keys starting with <c>$</c> are constants and are returned verbatim, without further expansion.</param>
		/// <param name="args">Optional arguments applied with <see cref="string.Format(string, object[])"/> after rendering; <see langword="null"/> or empty skips formatting entirely. A provider has no language to select plural forms in, so a <c>{0#word}</c> selector needs the <see cref="IWords"/> overload.</param>
		/// <returns>The rendered text; never <see langword="null"/>.</returns>
		[return: NotNull, Localized]
		public static string RenderKey(
				[DisallowNull] IWordsProvider wordsProvider,
				[DisallowNull] string key,
				[AllowNull] object[] args = null) {
			ArgumentNullException.ThrowIfNull(wordsProvider);
			ArgumentNullException.ThrowIfNull(key);

			var renderedText = RenderKeyCore(wordsProvider, key, null);
			if (args?.Length > 0) {
				renderedText = string.Format(renderedText, args: args);
			}
			return renderedText;
		}
		/// <summary>
		/// Expands every <c>{$constant}</c> and <c>{&gt;key}</c> reference in
		/// <paramref name="text"/> itself, without looking the text up by key first.
		/// Escaped pairs (<c>\\</c>, <c>''</c>, <c>""</c>, <c>{{</c>) collapse to their
		/// single character. Missing references render as <c>#key#</c> and warn via
		/// <see cref="Logger"/>.
		/// </summary>
		/// <param name="wordsProvider">The dictionary to resolve references against.</param>
		/// <param name="text">The template text to render.</param>
		/// <param name="baseKey">Resolves relative references: <c>{&gt;.sub}</c> becomes <c>baseKey.sub</c>. If <see langword="null"/> or empty, the leading dot is simply dropped.</param>
		/// <param name="args">Optional arguments applied with <see cref="string.Format(string, object[])"/> after rendering; <see langword="null"/> or empty skips formatting entirely. A <c>{0#word}</c> selector needs the <see cref="IWords"/> overload.</param>
		/// <returns>The rendered text; never <see langword="null"/>.</returns>
		[return: NotNull, Localized]
		public static string RenderText(
				[DisallowNull] IWordsProvider wordsProvider,
				[DisallowNull] string text,
				[AllowNull] string baseKey = null,
				[AllowNull] object[] args = null) {
			ArgumentNullException.ThrowIfNull(wordsProvider);
			ArgumentNullException.ThrowIfNull(text);

			var renderedText = RenderTextCore(wordsProvider, text, baseKey, null);
			if (args?.Length > 0) {
				renderedText = string.Format(renderedText, args: args);
			}
			return renderedText;
		}

		[return: NotNull, Localized]
		private static string RenderKeyCore(
				[DisallowNull] IWordsProvider wordsProvider,
				[DisallowNull] string key,
				[AllowNull] Stack<string> path) {
			if (path?.Contains(key) == true) {
				var trail = string.Join("` <- `", path);
				Logger.Warn($"WORDS:CIRC:`{key}` <- `{trail}`");
				return $"# ∞ #";
			}
			if (key.StartsWith('$')) {
				if (wordsProvider.TryGetValue(key, out var constant)) {
					return constant;
				}
				else {
					Logger.Warn($"WORDS:CONST:`{key}`");
					return $"#{key}#";
				}
			}
			if (!wordsProvider.TryGetValue(key, out var value)) {
				Logger.Warn($"WORDS:KEY:`{key}`");
				return $"#{key}#";
			}
			path ??= new Stack<string>();
			path.Push(key);
			try {
				return RenderTextCore(wordsProvider, value, key, path);
			}
			finally {
				path.Pop();
			}
		}
		[return: Localized]
		private static string RenderTextCore(
				IWordsProvider wordsProvider,
				string text,
				string? baseKey,
				Stack<string>? path) {
			if (!rxUnescape.TryMatch(text, out var match)) {
				return text;
			}

			var result = new StringBuilder();
			var start = 0;
			while (match.Success) {
				result.Append(text, start, match.Index - start);

				if (match.Groups[1].Length == 1) {
					result.Append(match.Groups[1].Value);
				}
				else {
					var key = match.Groups[2].Value;
					switch (match.Value[1]) {
						case '$':
							result.Append(RenderKeyCore(wordsProvider, "$" + key, path));
							break;
						case '>':
							if (key.StartsWith('.')) {
								if (string.IsNullOrEmpty(baseKey)) {
									key = key[1..];
								}
								else {
									key = baseKey + key;
								}
							}
							result.Append(RenderKeyCore(wordsProvider, key, path));
							break;
						default:
							throw new InvalidOperationException($"unexpected symbol: '{match.Value[1]}'");
					}
				}

				start = match.Index + match.Length;
				match = match.NextMatch();
			}
			if (start != text.Length) {
				result.Append(text, start, text.Length - start);
			}
			return result.ToString();
		}

		[return: Localized]
		internal static string RenderCount(IWords words, string key, decimal count) {
			ArgumentNullException.ThrowIfNull(key);
			string language = words.UICulture.Name;
			return words[FormKey(words.Provider, language, key, PluralRules.Select(language, count))];
		}

		//the entry a count reads (SPEC: Plural forms): key#form, else the form an optional
		//category reads instead, else key#other, else the plain value, which is the one
		//form; a language with one category has only that
		private static string FormKey(IWordsProvider provider, string language, string key, string form) {
			if (form != "one" && PluralRules.Categories(language).Count > 1) {
				if (provider.ContainsKey($"{key}#{form}")) {
					return $"{key}#{form}";
				}
				if (PluralRules.Optional(language).TryGetValue(form, out string? standIn) && provider.ContainsKey($"{key}#{standIn}")) {
					return $"{key}#{standIn}";
				}
				if (provider.ContainsKey($"{key}#other")) {
					return $"{key}#other";
				}
			}
			return key;
		}

		//each {n#key} in template becomes the form its argument selects, rendered, and
		//selected through in turn: a form may select too. key's own forms are out of
		//reach of its template, as a key is of its own references
		[return: Localized]
		private static string SelectForms(IWords words, string template, string key, Func<string, object?> argument) {
			if (!template.Contains('#')) {
				return template;
			}
			var path = new Stack<string>();
			path.Push(key);
			return SelectForms(words, template, key, argument, path);
		}
		[return: Localized]
		private static string SelectForms(IWords words, string template, string baseKey, Func<string, object?> argument, Stack<string> path) {
			return rxSelector.Replace(template, match => {
				if (!match.Groups[2].Success) {
					return match.Value;
				}
				string key = match.Groups[2].Value;
				if (key.StartsWith('.')) {
					key = baseKey == "" ? key[1..] : baseKey + key;
				}
				if (path.Contains(key)) {
					Logger.Warn($"WORDS:CIRC:`{key}` <- `{string.Join("` <- `", path)}`");
					return "# ∞ #";
				}
				string language = words.UICulture.Name;
				string form = FormKey(words.Provider, language, key, Category(language, argument(match.Groups[1].Value)));
				path.Push(key);
				try {
					return SelectForms(words, words[form], key, argument, path);
				}
				finally {
					path.Pop();
				}
			});
		}

		//the form an argument picks: a count by CLDR's rule; null, a value not there yet,
		//picks other quietly, and anything else picks other with a warning
		private static string Category(string language, object? value) {
			if (TryCount(value, out decimal count)) {
				return PluralRules.Select(language, count);
			}
			if (value is not null) {
				Logger.Warn($"WORDS:COUNT:`{value}`");
			}
			return "other";
		}
		private static bool TryCount(object? value, out decimal count) {
			switch (value) {
				case sbyte or byte or short or ushort or int or uint or long or ulong or decimal:
					count = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
					return true;
				case double d when double.IsFinite(d) && Math.Abs(d) < 7.9e28:
					count = (decimal)d;
					return true;
				case float f when float.IsFinite(f) && Math.Abs(f) < 7.9e28f:
					count = (decimal)f;
					return true;
				default:
					count = 0;
					return false;
			}
		}

		//a selector's argument by number, where string.Format finds it; a name is none
		//of the positional arguments, and picks other with a warning
		private static Func<string, object?> Positional(object?[]? args) => name => {
			if (!char.IsDigit(name[0])) {
				Logger.Warn($"WORDS:FIELD:`{name}`");
				return null;
			}
			if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index >= (args?.Length ?? 0)) {
				throw new FormatException($"{{{name}#…}}: index (zero based) must be less than the size of the argument list.");
			}
			return args![index];
		};
		//by name, a public field or property of value, as FormatByName reads it; by number,
		//a positional argument, and the one past them value itself (PreFormatByName's slot)
		private static Func<string, object?> Named(object? value, object?[]? args) {
			var positional = Positional(args);
			int count = args?.Length ?? 0;
			return name => {
				if (char.IsDigit(name[0])) {
					return int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index == count
						? value
						: positional(name);
				}
				if (TryMember(value, name, out var found)) {
					return found;
				}
				Logger.Warn($"WORDS:FIELD:`{name}`");
				return null;
			};
		}

		//a public field or property by name; a null item has every member, as null
		private static bool TryMember(object? item, string name, out object? found) {
			found = null;
			if (item is null) {
				return true;
			}
			var type = item.GetType();
			if (type.GetField(name) is FieldInfo field) {
				found = field.GetValue(item);
				return true;
			}
			if (type.GetProperty(name) is PropertyInfo property) {
				found = property.GetValue(item);
				return true;
			}
			return false;
		}

		/// <summary>
		/// Retrieves and renders the value of <paramref name="key"/> with silent failure.
		/// </summary>
		/// <param name="words">The dictionary to read.</param>
		/// <param name="key">The key to look up.</param>
		/// <returns>The rendered value, or <see langword="null"/> if the key is not present.</returns>
		[return: MaybeNull, Localized]
		public static string TryGetValue(
				[DisallowNull] this IWords words,
				[DisallowNull] string key) {
			if (words.TryGetValue(key, out var value)) {
				return value;
			}
			else {
				return null;
			}
		}
		/// <inheritdoc cref="Format(IWords, IFormatProvider?, string, object?[])"/>
		[return: Localized]
		public static string Format(this IWords known, string key, params object?[] args)
			=> Format(known, null, key, args);
		/// <summary>
		/// Looks up <paramref name="key"/> and applies <paramref name="args"/> to its
		/// <c>{0}</c>-style placeholders, exactly like
		/// <see cref="string.Format(IFormatProvider, string, object[])"/> but with a
		/// words key instead of a format string. First each plural selector,
		/// <c>{0#word}</c>, is replaced by the form of <c>word</c> its argument's count
		/// picks (<see cref="IWords.this[string, decimal]"/>), so <c>{0} {0#word}</c>
		/// reads "1 Word" and "2 Words"; the argument itself prints only where a
		/// <c>{0}</c> puts it.
		/// </summary>
		/// <param name="known">The dictionary to read.</param>
		/// <param name="provider">Culture-specific formatting, or <see langword="null"/> for the current culture.</param>
		/// <param name="key">The key of the format template.</param>
		/// <param name="args">The values to format into the template.</param>
		[return: Localized]
		public static string Format(this IWords known, IFormatProvider? provider, string key, params object?[] args)
			=> string.Format(provider, SelectForms(known, known[key], key, Positional(args)), args);

		/// <inheritdoc cref="FormatByName(IWords, IFormatProvider?, string, object?, object?[])"/>
		[return: Localized]
		public static string FormatByName(this IWords known, string key, object? value, params object?[] args)
			=> FormatByName(known, null, key, value, args);
		/// <summary>
		/// Looks up <paramref name="key"/> and formats it with named placeholders:
		/// <c>{PropertyName}</c> tags are filled from public fields and properties of
		/// <paramref name="value"/>, while numbered <c>{0}</c>-style tags still refer to
		/// <paramref name="args"/>. See <see cref="PreFormatByName(string, object?, object?[])"/>
		/// for the placeholder rules. A plural selector names its count either way:
		/// <c>{Count#word}</c> or <c>{0#word}</c>.
		/// </summary>
		/// <param name="known">The dictionary to read.</param>
		/// <param name="provider">Culture-specific formatting, or <see langword="null"/> for the current culture.</param>
		/// <param name="key">The key of the format template.</param>
		/// <param name="value">The object whose members are read by name.</param>
		/// <param name="args">Additional positional arguments.</param>
		[return: Localized]
		public static string FormatByName(this IWords known, IFormatProvider? provider, string key, object? value, params object?[] args)
			=> FormatByName(provider, SelectForms(known, known[key], key, Named(value, args)), value, args);

		/// <summary>
		/// Looks up <paramref name="key"/> and fills its placeholders from whatever
		/// <paramref name="params"/> is — the one rule the XAML inlines and converters
		/// share: an array supplies positional <c>{0}</c>-style arguments, any other object
		/// supplies <c>{Name}</c> placeholders read off its public fields and properties
		/// (<see cref="FormatByName(IFormatProvider?, string, object?, object?[])"/>), and
		/// <see langword="null"/> means no arguments at all — the text comes back as it is,
		/// plural selectors and all. Either kind of argument selects plural forms.
		/// </summary>
		/// <param name="known">The dictionary to read.</param>
		/// <param name="key">The key of the format template.</param>
		/// <param name="params">An array, a named-value object, or <see langword="null"/>.</param>
		/// <param name="provider">Culture-specific formatting, or <see langword="null"/> for the current culture.</param>
		[return: Localized]
		public static string FormatParams(this IWords known, string key, object? @params, IFormatProvider? provider = null) {
			ArgumentNullException.ThrowIfNull(known);
			var template = known[key];
			switch (@params) {
				case null:
					return template;
				case object[] args:
					return string.Format(provider, SelectForms(known, template, key, Positional(args)), args);
				case Array array: {
					var args = array.Cast<object?>().ToArray();
					return string.Format(provider, SelectForms(known, template, key, Positional(args)), args);
				}
				default:
					return FormatByName(provider, SelectForms(known, template, key, Named(@params, null)), @params);
			}
		}

		/// <summary>
		/// The body the frameworks' <c>WordsConverter</c>s share: <paramref name="value"/>
		/// formatted into the template <paramref name="parameter"/> names, per
		/// <see cref="FormatParams"/> — except that a <see langword="null"/> value fills the
		/// placeholders with nothing rather than showing the template, since a bound
		/// value that isn't there yet should not show markup. A missing or non-string
		/// parameter warns and yields the value — or the parameter — itself, hash-wrapped
		/// and truncated to 20 characters (<c>#value#</c>), so the mistake shows on screen.
		/// </summary>
		/// <param name="known">The dictionary to read.</param>
		/// <param name="value">The bound value to localize.</param>
		/// <param name="parameter">The key of the template, as the converter parameter.</param>
		/// <param name="provider">Culture-specific formatting, or <see langword="null"/> for the current culture.</param>
		/// <param name="logger">Hears about a missing or wrong-typed parameter; <see langword="null"/> discards it.</param>
		[return: Localized]
		public static string ConvertValue(this IWords known, object? value, object? parameter, IFormatProvider? provider, ITakeException? logger = null) {
			ArgumentNullException.ThrowIfNull(known);
			logger ??= ITakeException.Dummy;
			switch (parameter) {
				case null:
					logger.Warn("WORDS: ConverterParameter not specified.");
					return $"#{Truncate(value?.ToString())}#";
				case string key:
					// typed: a bare null would pick the dictionary overload, which refuses it
					return value is null
						? known.FormatByName(provider, key, (object?)null)
						: known.FormatParams(key, value, provider);
				default: {
					var text = Truncate(parameter.ToString());
					logger.Warn($"WORDS: ConverterParameter expecting string, found `{text}`");
					return $"#{text}#";
				}
			}

			static string Truncate(string? text) {
				text ??= "";
				return text.Length > 20 ? text[..20] : text;
			}
		}

		/// <summary>
		/// Takes a format template with named placeholders, and replaces the names with numbers.
		/// The returned argument array holds <paramref name="args"/> first, then the
		/// <paramref name="value"/> object itself, then each named member in order of first
		/// appearance; a repeated name reuses its original slot. A name that matches neither
		/// a public field nor a property formats as <c>#name#</c> and warns via
		/// <see cref="Logger"/>. Numbered placeholders pass through untouched and keep
		/// referring to <paramref name="args"/>.
		/// </summary>
		/// <param name="template">The format template containing <c>{Name}</c> or <c>{Name:format}</c> tags.</param>
		/// <param name="value">The object whose public fields and properties are read by name.</param>
		/// <param name="args">Additional positional arguments, addressed by the template's numbered tags.</param>
		/// <returns>A numbered format string and the matching argument array, ready for <see cref="string.Format(string, object[])"/>.</returns>
		public static (string FormatString, object?[] FormatArgs) PreFormatByName(string template, object? value, params object?[] args) {
			return PreFormatByName(template, value, name => {
				if (TryMember(value, name, out var found)) {
					return found;
				}
				Logger.Warn($"WORDS:FIELD:`{name}`");
				return $"#{name}#";
			}, args);
		}
		/// <summary>
		/// <see cref="PreFormatByName(string, object?, object?[])"/> with the named
		/// values supplied by a dictionary instead of an object's members — for
		/// callers that assemble them at runtime, such as an authoring tool trying
		/// out sample parameters. A name absent from <paramref name="values"/>
		/// formats as <c>#name#</c> and warns via <see cref="Logger"/>.
		/// </summary>
		/// <param name="template">The format template containing <c>{Name}</c> or <c>{Name:format}</c> tags.</param>
		/// <param name="values">The named values, by the name the template uses.</param>
		/// <param name="args">Additional positional arguments, addressed by the template's numbered tags.</param>
		/// <returns>A numbered format string and the matching argument array, ready for <see cref="string.Format(string, object[])"/>.</returns>
		public static (string FormatString, object?[] FormatArgs) PreFormatByName(string template, IReadOnlyDictionary<string, object?> values, params object?[] args) {
			ArgumentNullException.ThrowIfNull(values);
			return PreFormatByName(template, null, name => {
				if (values.TryGetValue(name, out var found)) {
					return found;
				}
				Logger.Warn($"WORDS:FIELD:`{name}`");
				return $"#{name}#";
			}, args);
		}
		private static (string FormatString, object?[] FormatArgs) PreFormatByName(string template, object? value, Func<string, object?> resolve, object?[] args) {
			// slot 0 after the positional args holds the source object itself, then
			// one slot per distinct name in order of first appearance
			var newArgs = new List<PairObj> { new PairObj("", value) };
			template = rxFormatTag.Replace(template, m => {
				string name = m.Groups[1].Value;
				int i = newArgs.FindIndex(n => n.Key == name);
				if (i == -1) {
					i = newArgs.Count;
					newArgs.Add(new PairObj(name, resolve(name)));
				}
				return $"{{{i + args.Length}:{m.Groups[2].Value}}}";
			});
			var newValues = args
				.Concat(newArgs.Select(n => n.Value))
				.ToArray();
			return (template, newValues);
		}
		/// <inheritdoc cref="FormatKnown(IFormatProvider?, string, object?[])"/>
		[return: Localized]
		public static string FormatKnown(string key, params object?[] args)
			=> FormatKnown(null, key, args);
		/// <summary>
		/// <see cref="Format(IWords, IFormatProvider?, string, object?[])"/> against the
		/// process-wide <see cref="Known"/> dictionary.
		/// </summary>
		/// <param name="provider">Culture-specific formatting, or <see langword="null"/> for the current culture.</param>
		/// <param name="key">The key of the format template.</param>
		/// <param name="args">The values to format into the template.</param>
		[return: Localized]
		public static string FormatKnown(IFormatProvider? provider, string key, params object?[] args)
			=> Known.Format(provider, key, args);

		/// <inheritdoc cref="FormatKnownByName(IFormatProvider?, string, object?, object?[])"/>
		public static string FormatKnownByName(string key, object? value, params object?[] args)
			=> Known.FormatByName(null, key, value, args);
		/// <summary>
		/// <see cref="FormatByName(IWords, IFormatProvider?, string, object?, object?[])"/>
		/// against the process-wide <see cref="Known"/> dictionary.
		/// </summary>
		/// <param name="provider">Culture-specific formatting, or <see langword="null"/> for the current culture.</param>
		/// <param name="key">The key of the format template.</param>
		/// <param name="value">The object whose members are read by name.</param>
		/// <param name="args">Additional positional arguments.</param>
		public static string FormatKnownByName(IFormatProvider? provider, string key, object? value, params object?[] args)
			=> Known.FormatByName(provider, key, value, args);

		/// <inheritdoc cref="FormatByName(IFormatProvider?, string, object?, object?[])"/>
		public static string FormatByName(string template, object? value, params object?[] args)
			=> FormatByName(provider: null, template, value, args);
		/// <summary>
		/// Formats a raw template string with named placeholders, no dictionary lookup involved.
		/// See <see cref="PreFormatByName(string, object?, object?[])"/> for the placeholder rules.
		/// </summary>
		/// <param name="provider">Culture-specific formatting, or <see langword="null"/> for the current culture.</param>
		/// <param name="template">The format template containing <c>{Name}</c> or <c>{Name:format}</c> tags.</param>
		/// <param name="value">The object whose public fields and properties are read by name.</param>
		/// <param name="args">Additional positional arguments, addressed by the template's numbered tags.</param>
		public static string FormatByName(IFormatProvider? provider, string template, object? value, params object?[] args) {
			var (formatString, formatArgs) = PreFormatByName(template, value, args);
			return string.Format(provider, formatString, formatArgs);
		}

		/// <inheritdoc cref="FormatByName(IFormatProvider?, string, IReadOnlyDictionary{string, object?}, object?[])"/>
		public static string FormatByName(string template, IReadOnlyDictionary<string, object?> values, params object?[] args)
			=> FormatByName(provider: null, template, values, args);
		/// <summary>
		/// Formats a raw template string with named placeholders filled from a
		/// dictionary, no dictionary lookup and no reflection involved.
		/// See <see cref="PreFormatByName(string, IReadOnlyDictionary{string, object?}, object?[])"/>.
		/// </summary>
		/// <param name="provider">Culture-specific formatting, or <see langword="null"/> for the current culture.</param>
		/// <param name="template">The format template containing <c>{Name}</c> or <c>{Name:format}</c> tags.</param>
		/// <param name="values">The named values, by the name the template uses.</param>
		/// <param name="args">Additional positional arguments, addressed by the template's numbered tags.</param>
		public static string FormatByName(IFormatProvider? provider, string template, IReadOnlyDictionary<string, object?> values, params object?[] args) {
			var (formatString, formatArgs) = PreFormatByName(template, values, args);
			return string.Format(provider, formatString, formatArgs);
		}
	}

	/// <summary>
	/// An <see cref="IWords"/> that knows no words: every lookup echoes the key back
	/// as <c>#key#</c>. Useful in tests or previews where seeing the key is more
	/// helpful than seeing a translation.
	/// </summary>
	public class EchoWords : IWords {
		/// <summary>
		/// Always the empty provider; nothing is stored here.
		/// </summary>
		public IWordsProvider Provider => WordsProvider.Empty();

		/// <summary>
		/// Returns <paramref name="key"/> wrapped as <c>#key#</c>, for any key at all.
		/// </summary>
		[NotNull, Localized]
		public string this[[DisallowNull] string key] => $"#{key}#";

		/// <inheritdoc cref="this[string]"/>
		[return: NotNull, Localized]
		public string GetValue([DisallowNull] string key) => this[key];
		/// <summary>
		/// Always succeeds, outputting <c>#key#</c>. Note the asymmetry with
		/// <see cref="ContainsKey(string)"/>, which always reports <see langword="false"/>.
		/// </summary>
		/// <param name="key">The key to echo.</param>
		/// <param name="value">Receives <c>#key#</c>.</param>
		/// <returns>Always <see langword="true"/>.</returns>
		public bool TryGetValue([DisallowNull] string key, [MaybeNullWhen(false), Localized] out string value) {
			value = this[key];
			return true;
		}
		/// <summary>
		/// Always <see langword="false"/>; no key is genuinely known, they are merely echoed.
		/// </summary>
		public bool ContainsKey([AllowNull] string key) => false;

		/// <summary>
		/// Does nothing; the echo has no culture.
		/// </summary>
		public void SetCulture() { }
	}

	/// <summary>
	/// Holds a key and defers the <see cref="Words.Known"/> lookup until
	/// <see cref="Value"/> is first read. Intended for services that initialise
	/// statically, before the dictionary has been loaded at startup — and, in live
	/// mode (<see cref="Words.Live"/>), the proxy a binding follows: it registers when
	/// it first resolves, and a swap of the dictionary drops its cache and raises
	/// <see cref="PropertyChanged"/>, so the next read is the new language.
	/// </summary>
	[DebuggerDisplay("LazyWords({Key} -> {Value})")]
	public class LazyWords : INotifyPropertyChanged, IKnowWords {
		/// <summary>
		/// A <see cref="LazyWords"/> whose value is the empty string; no lookup ever occurs.
		/// </summary>
		public static readonly LazyWords Empty = string.Empty;

		//one proxy per key for the bindings that share it; weak, so a key nobody binds
		//any more goes, and its husk is overwritten the next time the key is asked for
		private static readonly Dictionary<string, WeakReference<LazyWords>> shared = new();

		/// <summary>
		/// The one <see cref="LazyWords"/> for <paramref name="key"/> that every caller
		/// shares while anything holds it — what a live <c>{l:Words}</c> binds to, so a
		/// window with forty labels over forty keys carries forty proxies, not one per
		/// visual.
		/// </summary>
		/// <param name="key">The key to share a holder for.</param>
		/// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
		public static LazyWords Of(string key) {
			ArgumentNullException.ThrowIfNull(key);
			lock (shared) {
				if (shared.TryGetValue(key, out var weak) && weak.TryGetTarget(out var proxy)) {
					return proxy;
				}
				proxy = new LazyWords(key);
				shared[key] = new WeakReference<LazyWords>(proxy);
				return proxy;
			}
		}

		private string _Key;
		/// <summary>
		/// The key that will be resolved against <see cref="Words.Known"/> on first read
		/// of <see cref="Value"/>. Cannot be <see langword="null"/>.
		/// </summary>
		/// <exception cref="ArgumentNullException">The value assigned is <see langword="null"/>.</exception>
		[NotNull, DisallowNull]
		public string Key {
			get => _Key;
			[MemberNotNull(nameof(_Key))]
			set {
				if (value is null) {
					throw new ArgumentNullException(nameof(value), "Key cannot be null");
				}

				_Key = value;
			}
		}

		[AllowNull, MaybeNull]
		private string _Value;
		private bool literal;
		/// <summary>
		/// The resolved text. First read looks up <see cref="Key"/> in
		/// <see cref="Words.Known"/> and caches the result; in live mode, a read also
		/// registers this holder to be refreshed on a swap. Assigning a value beforehand
		/// makes it a literal — text, not a key — that never looks up and never refreshes;
		/// <see langword="null"/> clears the cache and the literal both.
		/// </summary>
		[AllowNull, Localized]
		public string Value {
			get {
				if (literal) {
					return _Value!;
				}
				_Value ??= Words.Known[Key];
				//registration is at resolution, not construction: a literal never gets
				//here, and an unread holder stays out of the registry until it is read
				Words.Watch(this);
				return _Value;
			}
			set {
				_Value = value;
				literal = value is not null;
			}
		}

		/// <inheritdoc/>
		public event PropertyChangedEventHandler? PropertyChanged;

		/// <summary>
		/// The dictionary was swapped: drop the cached text so the next read resolves
		/// again, and raise <see cref="PropertyChanged"/> for <see cref="Value"/> so a
		/// binding over it re-pulls. A literal is left as it is.
		/// </summary>
		public void Refresh() {
			if (literal) {
				return;
			}
			_Value = null;
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
		}

		/// <summary>
		/// Creates a holder for <paramref name="key"/>; nothing is looked up yet.
		/// </summary>
		/// <param name="key">The key to resolve later.</param>
		/// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
		public LazyWords(string key) {
			ArgumentNullException.ThrowIfNull(key);

			Key = key;
		}

		/// <summary>
		/// Wraps a literal string as an already-resolved <see cref="LazyWords"/> with the
		/// placeholder key <c>"*"</c>; no dictionary lookup will occur.
		/// </summary>
		public static implicit operator LazyWords(string en)
#pragma warning disable PTL001 // Expecting localized value
			=> new LazyWords("*") { Value = en };
#pragma warning restore PTL001 // Expecting localized value
		/// <summary>
		/// Resolves and returns <see cref="Value"/>, triggering the lookup if it
		/// has not happened yet.
		/// </summary>
		public static implicit operator string(LazyWords words)
			=> words.Value;

		/// <summary>
		/// Resolves and returns <see cref="Value"/>.
		/// </summary>
		public override string ToString() => Value;
	}
}
