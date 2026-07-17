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

	public interface IWords {
		IWordsProvider Provider { get; }

		[Localized]
		string this[string key] { get; }

		bool ContainsKey(string key);
		bool TryGetValue(string key, [MaybeNullWhen(false), Localized] out string value);

		void SetCulture();
	}

	public static class Words {
		private static IWords _Known = new Wordsmith(WordsProvider.Empty(), System.Globalization.CultureInfo.InvariantCulture);
		private static readonly Regex rxFormatTag = new(
				@"\{[\s-[\r\n]]*(?<1>(?=[_a-zA-Z])\w+)[\s-[\r\n]]*(:[\s-[\r\n]]*(?<2>[^\r\n}]*(?<!\s))[\s-[\r\n]]*)?\}",
				RegexOptions.Compiled | RegexOptions.ExplicitCapture);
		private static readonly Regex rxUnescape = new(
				@"(?<1>[\\'""{])\1|\{[$>](?<2>[^}]+)\}",
				RegexOptions.Compiled | RegexOptions.ExplicitCapture);

		[DisallowNull, NotNull]
		public static IWords Known {
			get => Volatile.Read(ref _Known);
			set {
				ArgumentNullException.ThrowIfNull(value);
				Volatile.Write(ref _Known, value);
				value.SetCulture();
			}
		}
		public static ITakeException Logger = ITakeException.Dummy;

		public static WordsBuilder Builder() => WordsBuilder.Create();

		[return: NotNull, Localized]
		public static string RenderKey(
				[DisallowNull] this IWords words,
				[DisallowNull] string key,
				[AllowNull] object[] args = null) {
			return RenderKey(words.Provider, key, args);
		}
		[return: NotNull, Localized]
		public static string RenderText(
				[DisallowNull] this IWords words,
				[DisallowNull] string text,
				[AllowNull] string baseKey = null,
				[AllowNull] object[] args = null) {
			return RenderText(words.Provider, text, baseKey, args);
		}

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
		[return: Localized]
		public static string Format(this IWords known, string key, params object?[] args)
			=> Format(known, null, key, args);
		[return: Localized]
		public static string Format(this IWords known, IFormatProvider? provider, string key, params object?[] args)
			=> string.Format(provider, known[key], args);

		[return: Localized]
		public static string FormatByName(this IWords known, string key, object? value, params object?[] args)
			=> FormatByName(known[key], value, args);
		[return: Localized]
		public static string FormatByName(this IWords known, IFormatProvider? provider, string key, object? value, params object?[] args)
			=> FormatByName(provider, known[key], value, args);

		/// <summary>
		/// Takes a format template with named placeholders, and replaces the names with numbers. The first item is a
		/// direct reference to the object being passed, followed by any public instance field/property it finds with
		/// the specified name.
		/// </summary>
		/// <param name="template"></param>
		/// <param name="value"></param>
		/// <param name="args"></param>
		/// <returns></returns>
		public static (string FormatString, object?[] FormatArgs) PreFormatByName(string template, object? value, params object?[] args) {
			var newArgs = new List<PairObj> { new PairObj("", value) };
			var type = value?.GetType() ?? typeof(void);
			template = rxFormatTag.Replace(template, m => {
				string name = m.Groups[1].Value;
				int i = newArgs.FindIndex(n => n.Key == name);
				if (i == -1) {
					i = newArgs.Count + args.Length;
					var newValue = valueOf(value, name, type);
					newArgs.Add(new PairObj(name, newValue));
				}
				return $"{{{i}:{m.Groups[2].Value}}}";
			});
			var newValues = args
				.Concat(newArgs.Select(n => n.Value))
				.ToArray();
			return (template, newValues);
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
		[return: Localized]
		public static string FormatKnown(string key, params object?[] args)
			=> FormatKnown(null, key, args);
		[return: Localized]
		public static string FormatKnown(IFormatProvider? provider, string key, params object?[] args)
			=> string.Format(provider, Known[key], args);

		public static string FormatKnownByName(string key, object? value, params object?[] args)
			=> FormatByName(Known[key], value, args);
		public static string FormatKnownByName(IFormatProvider? provider, string key, object? value, params object?[] args)
			=> FormatByName(provider, Known[key], value, args);

		public static string FormatByName(string template, object? value, params object?[] args)
			=> FormatByName(provider: null, template, value, args);
		public static string FormatByName(IFormatProvider? provider, string template, object? value, params object?[] args) {
			var (formatString, formatArgs) = PreFormatByName(template, value, args);
			return string.Format(provider, formatString, formatArgs);
		}
	}

	public class EchoWords : IWords {
		public IWordsProvider Provider => WordsProvider.Empty();

		[NotNull, Localized]
		public string this[[DisallowNull] string key] => $"#{key}#";

		[return: NotNull, Localized]
		public string GetValue([DisallowNull] string key) => this[key];
		public bool TryGetValue([DisallowNull] string key, [MaybeNullWhen(false), Localized] out string value) {
			value = this[key];
			return true;
		}
		public bool ContainsKey([AllowNull] string key) => false;

		public void SetCulture() { }
	}

	[DebuggerDisplay("LazyWords({Key} -> {Value})")]
	public class LazyWords {
		public static readonly LazyWords Empty = string.Empty;

		private string _Key;
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

		public LazyWords(string key) {
			ArgumentNullException.ThrowIfNull(key);

			Key = key;
		}

		public static implicit operator LazyWords(string en)
#pragma warning disable PTL001 // Expecting localized value
			=> new LazyWords("*") { Value = en };
#pragma warning restore PTL001 // Expecting localized value
		public static implicit operator string(LazyWords words)
			=> words.Value;

		public override string ToString() => Value;
	}
}
