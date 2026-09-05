using PatTech.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
		/// Applies this dictionary's culture to the current thread and to the
		/// process-wide defaults, so number/date formatting matches the selected language.
		/// Called automatically when assigning <see cref="Words.Known"/>.
		/// </summary>
		void SetCulture();
	}

	/// <summary>
	/// This is Words. It gives you words.
	/// Home of the process-wide dictionary (<see cref="Known"/>), the reference-rendering
	/// engine (<see cref="RenderKey(IWordsProvider, string, object[])"/>), and the
	/// <c>String.Format</c>-style helpers, including named-parameter formatting.
	/// </summary>
	public static class Words {
		private static IWords _Known = new CulturedWords(WordsProvider.Empty(), System.Globalization.CultureInfo.InvariantCulture);
		private static readonly Regex rxFormatTag = new(
				@"\{[\s-[\r\n]]*(?<1>(?=[_a-zA-Z])\w+)[\s-[\r\n]]*(:[\s-[\r\n]]*(?<2>[^\r\n}]*(?<!\s))[\s-[\r\n]]*)?\}",
				RegexOptions.Compiled | RegexOptions.ExplicitCapture);
		private static readonly Regex rxUnescape = new(
				@"(?<1>[\\'""{])\1|\{[$>](?<2>[^}]+)\}",
				RegexOptions.Compiled | RegexOptions.ExplicitCapture);

		/// <summary>
		/// The process-wide dictionary, typically assigned once at startup from
		/// <see cref="WordsBuilder.ToWords(string, bool)"/>. Reads and writes are volatile,
		/// so the swap is safe from any thread. Assigning also calls
		/// <see cref="IWords.SetCulture"/> on the new value. Starts as an empty,
		/// invariant-culture dictionary, so every lookup renders as <c>#key#</c>
		/// until real words are loaded.
		/// </summary>
		/// <exception cref="ArgumentNullException">The value assigned is <see langword="null"/>.</exception>
		[DisallowNull, NotNull]
		public static IWords Known {
			get => Volatile.Read(ref _Known);
			set {
				ArgumentNullException.ThrowIfNull(value);
				Volatile.Write(ref _Known, value);
				value.SetCulture();
			}
		}
		/// <summary>
		/// Receives warnings about missing keys, unknown constants, circular references
		/// and absent format fields. Defaults to a logger that discards everything;
		/// assign your own to hear about your typos.
		/// </summary>
		public static ITakeException Logger = ITakeException.Dummy;

		/// <summary>
		/// Shorthand for <see cref="WordsBuilder.Create(ITakeException?)"/> with no logger.
		/// </summary>
		public static WordsBuilder Builder() => WordsBuilder.Create();

		/// <inheritdoc cref="RenderKey(IWordsProvider, string, object[])"/>
		[return: NotNull, Localized]
		public static string RenderKey(
				[DisallowNull] this IWords words,
				[DisallowNull] string key,
				[AllowNull] object[] args = null) {
			return RenderKey(words.Provider, key, args);
		}
		/// <inheritdoc cref="RenderText(IWordsProvider, string, string, object[])"/>
		[return: NotNull, Localized]
		public static string RenderText(
				[DisallowNull] this IWords words,
				[DisallowNull] string text,
				[AllowNull] string baseKey = null,
				[AllowNull] object[] args = null) {
			return RenderText(words.Provider, text, baseKey, args);
		}

		/// <summary>
		/// Looks up <paramref name="key"/> and expands every <c>{$constant}</c> and
		/// <c>{&gt;key}</c> reference it contains, recursively. A missing key or constant
		/// renders as <c>#key#</c>; a circular reference renders as <c># ∞ #</c>;
		/// both are reported to <see cref="Logger"/> rather than thrown.
		/// </summary>
		/// <param name="wordsProvider">The dictionary to resolve keys against.</param>
		/// <param name="key">The key to look up. Keys starting with <c>$</c> are constants and are returned verbatim, without further expansion.</param>
		/// <param name="args">Optional arguments applied with <see cref="string.Format(string, object[])"/> after rendering; <see langword="null"/> or empty skips formatting entirely.</param>
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
		/// <param name="args">Optional arguments applied with <see cref="string.Format(string, object[])"/> after rendering; <see langword="null"/> or empty skips formatting entirely.</param>
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
		/// words key instead of a format string.
		/// </summary>
		/// <param name="known">The dictionary to read.</param>
		/// <param name="provider">Culture-specific formatting, or <see langword="null"/> for the current culture.</param>
		/// <param name="key">The key of the format template.</param>
		/// <param name="args">The values to format into the template.</param>
		[return: Localized]
		public static string Format(this IWords known, IFormatProvider? provider, string key, params object?[] args)
			=> string.Format(provider, known[key], args);

		/// <inheritdoc cref="FormatByName(IWords, IFormatProvider?, string, object?, object?[])"/>
		[return: Localized]
		public static string FormatByName(this IWords known, string key, object? value, params object?[] args)
			=> FormatByName(known[key], value, args);
		/// <summary>
		/// Looks up <paramref name="key"/> and formats it with named placeholders:
		/// <c>{PropertyName}</c> tags are filled from public fields and properties of
		/// <paramref name="value"/>, while numbered <c>{0}</c>-style tags still refer to
		/// <paramref name="args"/>. See <see cref="PreFormatByName(string, object?, object?[])"/>
		/// for the placeholder rules.
		/// </summary>
		/// <param name="known">The dictionary to read.</param>
		/// <param name="provider">Culture-specific formatting, or <see langword="null"/> for the current culture.</param>
		/// <param name="key">The key of the format template.</param>
		/// <param name="value">The object whose members are read by name.</param>
		/// <param name="args">Additional positional arguments.</param>
		[return: Localized]
		public static string FormatByName(this IWords known, IFormatProvider? provider, string key, object? value, params object?[] args)
			=> FormatByName(provider, known[key], value, args);

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
			var type = value?.GetType() ?? typeof(void);
			return PreFormatByName(template, value, name => valueOf(value, name, type), args);
			static object? valueOf(object? item, string key, Type itemType) {
				if (item is null) { return null; }
				if (itemType.GetField(key) is FieldInfo k) {
					return k.GetValue(item);
				}
				if (itemType.GetProperty(key) is PropertyInfo p) {
					return p.GetValue(item);
				}
				Logger.Warn($"WORDS:FIELD:`{key}`");
				return $"#{key}#";
			}
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
			=> string.Format(provider, Known[key], args);

		/// <inheritdoc cref="FormatKnownByName(IFormatProvider?, string, object?, object?[])"/>
		public static string FormatKnownByName(string key, object? value, params object?[] args)
			=> FormatByName(Known[key], value, args);
		/// <summary>
		/// <see cref="FormatByName(IWords, IFormatProvider?, string, object?, object?[])"/>
		/// against the process-wide <see cref="Known"/> dictionary.
		/// </summary>
		/// <param name="provider">Culture-specific formatting, or <see langword="null"/> for the current culture.</param>
		/// <param name="key">The key of the format template.</param>
		/// <param name="value">The object whose members are read by name.</param>
		/// <param name="args">Additional positional arguments.</param>
		public static string FormatKnownByName(IFormatProvider? provider, string key, object? value, params object?[] args)
			=> FormatByName(provider, Known[key], value, args);

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
	/// statically, before the dictionary has been loaded at startup.
	/// </summary>
	[DebuggerDisplay("LazyWords({Key} -> {Value})")]
	public class LazyWords {
		/// <summary>
		/// A <see cref="LazyWords"/> whose value is the empty string; no lookup ever occurs.
		/// </summary>
		public static readonly LazyWords Empty = string.Empty;

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
		/// <summary>
		/// The resolved text. First read looks up <see cref="Key"/> in
		/// <see cref="Words.Known"/> and caches the result; assigning a value beforehand
		/// (or <see langword="null"/> to clear the cache) overrides the lookup.
		/// Writeable in the event that we don't care for a registered string, use this one.
		/// </summary>
		[AllowNull, Localized]
		public string Value {
			get {
				_Value ??= Words.Known[Key];
				return _Value;
			}
			set => _Value = value;
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
