using PatTech.Utils;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Numerics;
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
		string this[[WordsKey] string key] { get; }

		/// <summary>
		/// Returns <paramref name="key"/>'s own form for <paramref name="count"/>, rendered
		/// like any value: the plural form CLDR's rule for <see cref="Language"/> picks
		/// (SPEC: Plural forms), its <c>other</c> form when it has none for that count,
		/// and its plain value when it has neither — so <c>Words.Known["word", 2]</c> is
		/// "Words" where <c>Words.Known["word"]</c> is "Word". A missing key renders as
		/// <c>#key#</c>, as for the indexer.
		/// </summary>
		/// <param name="key">The key to look up.</param>
		/// <param name="count">The count to pick the form by; a fractional one picks <c>other</c>.</param>
		[Localized]
		string this[[WordsKey] string key, decimal count] => Words.RenderCount(this, key, count);

		/// <summary>
		/// The language this dictionary speaks, as a culture. A dictionary that does
		/// not say speaks the thread's UI culture.
		/// </summary>
		CultureInfo UICulture => CultureInfo.CurrentUICulture;

		/// <summary>
		/// The language whose plural rules pick a count's form (<see cref="PluralRules"/>):
		/// the code the dictionary was built for, which .NET may know only as the
		/// invariant culture — Cebuano, Ladin, the legacy <c>iw</c> and <c>tl</c>. A
		/// dictionary that does not say goes by <see cref="UICulture"/>'s name.
		/// </summary>
		string Language => UICulture.Name;

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
		//an escaped pair, a {$constant} or {>key} reference, or a {0#word} / {Count#word}
		//selector, in one pass, so each resolves against the key it was written in
		private static readonly Regex rxRender = new(
				@"(?<1>[\\'""{])\1|\{[$>](?<2>[^}]+)\}|\{(?<3>\d+|(?=[_a-zA-Z])\w+)#(?<4>[^{}#\s]+)\}",
				RegexOptions.Compiled | RegexOptions.ExplicitCapture);
		//a selector in rendered words, past string.Format's {{ pairs
		private static readonly Regex rxSelector = new(
				@"\{\{|\{(?<1>\d+|(?=[_a-zA-Z])\w+)#(?<2>[^{}#\s]+)\}",
				RegexOptions.Compiled | RegexOptions.ExplicitCapture);

		//what a selector picks a form with: the language whose rules count, and the
		//arguments, by number or name
		private sealed record Selecting(string Language, Func<string, object?> Argument);

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
				[DisallowNull, WordsKey] string key,
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
		/// <param name="baseKey">Resolves relative references and selectors: <c>{&gt;.sub}</c> becomes <c>baseKey.sub</c>. The text is not taken for <c>baseKey</c>'s own words, so it may refer to <c>baseKey</c> itself.</param>
		/// <param name="args">Optional arguments for the selectors and <see cref="string.Format(string, object[])"/>; <see langword="null"/> or empty skips both.</param>
		[return: NotNull, Localized]
		public static string RenderText(
				[DisallowNull] this IWords words,
				[DisallowNull] string text,
				[AllowNull] string baseKey = null,
				[AllowNull] object[] args = null) {
			ArgumentNullException.ThrowIfNull(words);
			ArgumentNullException.ThrowIfNull(text);
			if (!(args?.Length > 0)) {
				return RenderText(words.Provider, text, baseKey);
			}
			//the text is the caller's, not baseKey's words, so it may refer to baseKey and
			//select its forms, as it may without arguments
			return string.Format(RenderTextCore(words.Provider, text, baseKey, null, new(words.Language, Positional(args))), args);
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
				[DisallowNull, WordsKey] string key,
				[AllowNull] object[] args = null) {
			ArgumentNullException.ThrowIfNull(wordsProvider);
			ArgumentNullException.ThrowIfNull(key);

			var renderedText = RenderKeyCore(wordsProvider, key, null, null);
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

			var renderedText = RenderTextCore(wordsProvider, text, baseKey, null, null);
			if (args?.Length > 0) {
				renderedText = string.Format(renderedText, args: args);
			}
			return renderedText;
		}

		//entry's words, rendered against the key they are words of, however the entry
		//was reached. path holds the entries being rendered, for the circular cut
		[return: NotNull, Localized]
		private static string RenderKeyCore(
				[DisallowNull] IWordsProvider wordsProvider,
				[DisallowNull] string entry,
				[AllowNull] Stack<string> path,
				Selecting? selecting) {
			if (path?.Contains(entry) == true) {
				var trail = string.Join("` <- `", path);
				Logger.Warn($"WORDS:CIRC:`{entry}` <- `{trail}`");
				return $"# ∞ #";
			}
			if (entry.StartsWith('$')) {
				if (wordsProvider.TryGetValue(entry, out var constant)) {
					return constant;
				}
				else {
					Logger.Warn($"WORDS:CONST:`{entry}`");
					return $"#{entry}#";
				}
			}
			if (!wordsProvider.TryGetValue(entry, out var value)) {
				Logger.Warn($"WORDS:KEY:`{entry}`");
				return $"#{entry}#";
			}
			path ??= new Stack<string>();
			path.Push(entry);
			try {
				return RenderTextCore(wordsProvider, value, KeyOf(entry), path, selecting);
			}
			finally {
				path.Pop();
			}
		}

		//the key an entry is words of: the entry itself, or for a form (word#other) the
		//key it is a form of, so a form's {>.sub} is its key's sub
		private static string KeyOf(string entry) => entry.IndexOf('#') is > 0 and var mark ? entry[..mark] : entry;
		//text's escapes, references and, when selecting, selectors, in one pass: what a
		//reference brings in was rendered against its own key, and is not scanned again
		[return: Localized]
		private static string RenderTextCore(
				IWordsProvider wordsProvider,
				string text,
				string? baseKey,
				Stack<string>? path,
				Selecting? selecting) {
			if (!rxRender.TryMatch(text, out var match)) {
				return text;
			}

			var result = new StringBuilder();
			var start = 0;
			while (match.Success) {
				result.Append(text, start, match.Index - start);

				if (match.Groups[1].Success) {
					result.Append(match.Groups[1].Value);
				}
				else if (match.Groups[2].Success) {
					var key = match.Groups[2].Value;
					switch (match.Value[1]) {
						case '$':
							result.Append(RenderKeyCore(wordsProvider, "$" + key, path, selecting));
							break;
						case '>':
							result.Append(RenderKeyCore(wordsProvider, Relative(key, baseKey), path, selecting));
							break;
						default:
							throw new InvalidOperationException($"unexpected symbol: '{match.Value[1]}'");
					}
				}
				else if (selecting is null) {
					//a provider has no language to select in: the selector stays
					result.Append(match.Value);
				}
				else {
					result.Append(Select(wordsProvider, match.Groups[3].Value, Relative(match.Groups[4].Value, baseKey), path, selecting));
				}

				start = match.Index + match.Length;
				match = match.NextMatch();
			}
			if (start != text.Length) {
				result.Append(text, start, text.Length - start);
			}
			return result.ToString();
		}

		//.sub under baseKey; with no base the dot is dropped
		private static string Relative(string key, string? baseKey)
			=> !key.StartsWith('.') ? key : string.IsNullOrEmpty(baseKey) ? key[1..] : baseKey + key;

		//the form of key its argument selects, rendered against key and selected through
		//in turn: a key whose words are being rendered, plain or a form, is out of reach
		//of its own selectors, as a key is of its own references
		[return: Localized]
		private static string Select(IWordsProvider provider, string argument, string key, Stack<string>? path, Selecting selecting) {
			path ??= new Stack<string>();
			if (path.Any(entry => entry == key || entry.StartsWith(key + "#", StringComparison.Ordinal))) {
				Logger.Warn($"WORDS:CIRC:`{key}` <- `{string.Join("` <- `", path)}`");
				return "# ∞ #";
			}
			string form = FormKey(provider, selecting.Language, key, Category(selecting.Language, selecting.Argument(argument)));
			return RenderKeyCore(provider, form, path, selecting);
		}

		//entry's words from the provider, so each reference and selector resolves
		//against the key it was written in; an entry the provider lacks reads through
		//the indexer, as an IWords of one's own may answer it, and still selects
		[return: Localized]
		private static string Template(IWords words, string entry, Selecting? selecting)
			=> words.Provider.ContainsKey(entry) ? RenderKeyCore(words.Provider, entry, null, selecting) : Selected(words.Provider, words[entry], KeyOf(entry), selecting);

		//words an indexer handed over are rendered already, so only their selectors are
		//left, each against key; a {{ pair is string.Format's now, and stays
		[return: Localized]
		private static string Selected(IWordsProvider provider, string words, string key, Selecting? selecting)
			=> selecting is null ? words : rxSelector.Replace(words, match => match.Groups[1].Success
				? Select(provider, match.Groups[1].Value, Relative(match.Groups[2].Value, key), null, selecting)
				: match.Value);

		[return: Localized]
		internal static string RenderCount(IWords words, string key, decimal count) {
			ArgumentNullException.ThrowIfNull(key);
			string language = words.Language;
			return Template(words, FormKey(words.Provider, language, key, PluralRules.Select(language, count)), null);
		}

		/// <summary>
		/// The entry a count in <paramref name="form"/> reads in <paramref name="language"/>
		/// (SPEC: Plural forms): <c>key#form</c> when the provider has it, else the form an
		/// optional category reads instead (<see cref="PluralRules.Optional"/>), else
		/// <c>key#other</c>, else <paramref name="key"/> itself, the plain value, which is
		/// the <c>one</c> form. A language with one category reads only the plain value.
		/// </summary>
		/// <param name="provider">The flattened words to look in.</param>
		/// <param name="language">The language whose rules apply, e.g. <c>"mt"</c>.</param>
		/// <param name="key">The key whose forms to choose among.</param>
		/// <param name="form">The CLDR category, as <see cref="PluralRules.Select"/> names it.</param>
		/// <returns>An entry the provider can be asked for, or <paramref name="key"/>.</returns>
		public static string FormKey(IWordsProvider provider, string language, string key, string form) {
			ArgumentNullException.ThrowIfNull(provider);
			ArgumentNullException.ThrowIfNull(language);
			ArgumentNullException.ThrowIfNull(key);
			ArgumentNullException.ThrowIfNull(form);
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

		//the form an argument picks: a count by CLDR's rule; null, a value not there yet,
		//picks other quietly, and anything else picks other with a warning
		private static string Category(string language, object? value) {
			if (TryCount(value, out decimal count, out bool fraction)) {
				return fraction ? "other" : PluralRules.Select(language, count);
			}
			if (value is not null) {
				Logger.Warn($"WORDS:COUNT:`{value}`");
			}
			return "other";
		}
		//any number as a count. A floating-point fraction says so before a decimal could
		//round it whole (1.0000001f); a whole number past decimal's range keeps the low
		//digits a rule reads, as PluralRules.Select keeps them past 10^12
		private static bool TryCount(object? value, out decimal count, out bool fraction) {
			fraction = false;
			switch (value) {
				case sbyte or byte or short or ushort or int or uint or long or ulong or decimal:
					count = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
					return true;
				case nint n:
					count = n;
					return true;
				case nuint n:
					count = n;
					return true;
				case Int128 n:
					count = Whole(n);
					return true;
				case UInt128 n:
					count = Whole(n);
					return true;
				case BigInteger n:
					count = Whole(n);
					return true;
				case Half h:
					return TryCount((double)h, out count, out fraction);
				case float f:
					return TryCount((double)f, out count, out fraction);
				case double d when double.IsFinite(d):
					//decimal keeps a double's first 15 digits, so a longer whole one counts whole
					fraction = d != Math.Truncate(d);
					count = fraction ? 0 : Math.Abs(d) < 1e15 ? (decimal)d : Whole(new BigInteger(d));
					return true;
				default:
					count = 0;
					return false;
			}

			static decimal Whole(BigInteger n) {
				n = BigInteger.Abs(n);
				return n <= (BigInteger)decimal.MaxValue ? (decimal)n : (decimal)(n % 1_000_000_000_000) + 1_000_000_000_000m;
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

		//a dictionary's value by name, else a public field or property; a null item has
		//every member, as null. A dictionary answers with its values alone, so a
		//{Count} reads the entry and never the dictionary's own Count
		private static bool TryMember(object? item, string name, out object? found) {
			found = null;
			switch (item) {
				case null:
					return true;
				case IReadOnlyDictionary<string, object?> values:
					return values.TryGetValue(name, out found);
				case IDictionary<string, object?> values:
					return values.TryGetValue(name, out found);
				case IDictionary values:
					if (values.Contains(name)) {
						found = values[name];
						return true;
					}
					return false;
			}
			var type = item.GetType();
			if (Lookup(type) is { } tryGetValue) {
				object?[] call = [name, null];
				bool has = (bool)tryGetValue.Invoke(item, call)!;
				found = has ? call[1] : null;
				return has;
			}
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

		//a type's TryGetValue as a dictionary by name of any other value type, if it is one:
		//its IReadOnlyDictionary<string, T>, else its IDictionary<string, T>. Kept per type
		private static readonly ConcurrentDictionary<Type, MethodInfo?> lookups = new();
		private static MethodInfo? Lookup(Type type) => lookups.GetOrAdd(type, static type => {
			Type[] interfaces = type.GetInterfaces();
			Type? dictionary = interfaces.FirstOrDefault(face => ByName(face, typeof(IReadOnlyDictionary<,>)))
				?? interfaces.FirstOrDefault(face => ByName(face, typeof(IDictionary<,>)));
			return dictionary?.GetMethod(nameof(IDictionary<string, object>.TryGetValue));

			static bool ByName(Type face, Type definition)
				=> face.IsGenericType && face.GetGenericTypeDefinition() == definition && face.GenericTypeArguments[0] == typeof(string);
		});

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
		public static string Format(this IWords known, [WordsKey] string key, params object?[] args)
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
		public static string Format(this IWords known, IFormatProvider? provider, [WordsKey] string key, params object?[] args)
			=> string.Format(provider, Template(known, key, new(known.Language, Positional(args))), args);

		/// <inheritdoc cref="FormatByName(IWords, IFormatProvider?, string, object?, object?[])"/>
		[return: Localized]
		public static string FormatByName(this IWords known, [WordsKey] string key, object? value, params object?[] args)
			=> FormatByName(known, null, key, value, args);
		/// <summary>
		/// Looks up <paramref name="key"/> and formats it with named placeholders:
		/// <c>{PropertyName}</c> tags are filled from public fields and properties of
		/// <paramref name="value"/>, or from its values by name when it is a dictionary
		/// (for callers that assemble the names at runtime, such as an authoring tool
		/// trying out sample parameters), while numbered <c>{0}</c>-style tags still refer
		/// to <paramref name="args"/>. See <see cref="PreFormatByName(string, object?, object?[])"/>
		/// for the placeholder rules. A plural selector names its count either way:
		/// <c>{Count#word}</c> or <c>{0#word}</c>.
		/// </summary>
		/// <param name="known">The dictionary to read.</param>
		/// <param name="provider">Culture-specific formatting, or <see langword="null"/> for the current culture.</param>
		/// <param name="key">The key of the format template.</param>
		/// <param name="value">The object whose members, or the dictionary whose values, are read by name; <see langword="null"/> fills every name with nothing.</param>
		/// <param name="args">Additional positional arguments.</param>
		[return: Localized]
		public static string FormatByName(this IWords known, IFormatProvider? provider, [WordsKey] string key, object? value, params object?[] args)
			=> FormatByName(provider, Template(known, key, new(known.Language, Named(value, args))), value, args);

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
		public static string FormatParams(this IWords known, [WordsKey] string key, object? @params, IFormatProvider? provider = null) {
			ArgumentNullException.ThrowIfNull(known);
			switch (@params) {
				case null:
					return known[key];
				case object[] args:
					return known.Format(provider, key, args);
				case Array array:
					return known.Format(provider, key, array.Cast<object?>().ToArray());
				default:
					return known.FormatByName(provider, key, @params);
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
		public static string ConvertValue(this IWords known, object? value, [WordsKey] object? parameter, IFormatProvider? provider, ITakeException? logger = null) {
			ArgumentNullException.ThrowIfNull(known);
			logger ??= ITakeException.Dummy;
			switch (parameter) {
				case null:
					logger.Warn("WORDS: ConverterParameter not specified.");
					return $"#{Truncate(value?.ToString())}#";
				case string key:
					return value is null
						? known.FormatByName(provider, key, null)
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
		/// <param name="value">The object whose public fields and properties, or the dictionary whose values, are read by name.</param>
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
		/// <see cref="PreFormatByName(string, object?, object?[])"/>, which reads a
		/// dictionary by name itself: whichever overload a call binds to, a dictionary or
		/// a <see langword="null"/> reads the same. Kept for callers built against 1.4.0.
		/// </summary>
		[EditorBrowsable(EditorBrowsableState.Never)]
		public static (string FormatString, object?[] FormatArgs) PreFormatByName(string template, IReadOnlyDictionary<string, object?>? values, params object?[] args)
			=> PreFormatByName(template, (object?)values, args);
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
		public static string FormatKnown([WordsKey] string key, params object?[] args)
			=> FormatKnown(null, key, args);
		/// <summary>
		/// <see cref="Format(IWords, IFormatProvider?, string, object?[])"/> against the
		/// process-wide <see cref="Known"/> dictionary.
		/// </summary>
		/// <param name="provider">Culture-specific formatting, or <see langword="null"/> for the current culture.</param>
		/// <param name="key">The key of the format template.</param>
		/// <param name="args">The values to format into the template.</param>
		[return: Localized]
		public static string FormatKnown(IFormatProvider? provider, [WordsKey] string key, params object?[] args)
			=> Known.Format(provider, key, args);

		/// <inheritdoc cref="FormatKnownByName(IFormatProvider?, string, object?, object?[])"/>
		public static string FormatKnownByName([WordsKey] string key, object? value, params object?[] args)
			=> Known.FormatByName(null, key, value, args);
		/// <summary>
		/// <see cref="FormatByName(IWords, IFormatProvider?, string, object?, object?[])"/>
		/// against the process-wide <see cref="Known"/> dictionary.
		/// </summary>
		/// <param name="provider">Culture-specific formatting, or <see langword="null"/> for the current culture.</param>
		/// <param name="key">The key of the format template.</param>
		/// <param name="value">The object whose members are read by name.</param>
		/// <param name="args">Additional positional arguments.</param>
		public static string FormatKnownByName(IFormatProvider? provider, [WordsKey] string key, object? value, params object?[] args)
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
		/// <param name="value">The object whose public fields and properties, or the dictionary whose values, are read by name.</param>
		/// <param name="args">Additional positional arguments, addressed by the template's numbered tags.</param>
		public static string FormatByName(IFormatProvider? provider, string template, object? value, params object?[] args) {
			var (formatString, formatArgs) = PreFormatByName(template, value, args);
			return string.Format(provider, formatString, formatArgs);
		}
		/// <summary>
		/// <see cref="FormatByName(string, object?, object?[])"/>, which reads a dictionary
		/// by name itself: whichever overload a call binds to, a dictionary or a
		/// <see langword="null"/> reads the same. Kept for callers built against 1.4.0.
		/// </summary>
		[EditorBrowsable(EditorBrowsableState.Never)]
		public static string FormatByName(string template, IReadOnlyDictionary<string, object?>? values, params object?[] args)
			=> FormatByName(provider: null, template, (object?)values, args);
		/// <inheritdoc cref="FormatByName(string, IReadOnlyDictionary{string, object?}?, object?[])"/>
		[EditorBrowsable(EditorBrowsableState.Never)]
		public static string FormatByName(IFormatProvider? provider, string template, IReadOnlyDictionary<string, object?>? values, params object?[] args)
			=> FormatByName(provider, template, (object?)values, args);
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
		public string this[[DisallowNull, WordsKey] string key] => $"#{key}#";

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
		public static LazyWords Of([WordsKey] string key) {
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
		[NotNull, DisallowNull, WordsKey]
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
		public LazyWords([WordsKey] string key) {
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
