using PatTech.Localization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace PatTech.Utils {
	/// <summary>
	/// Extension methods for use with Words engine.
	/// </summary>
	public static class Extensions {

		/// <summary>
		///     The words for an enum value, as <paramref name="format"/> asks for them: the
		///     member's describable from <see cref="Describable"/>, built once from its type,
		///     described by <see cref="Describe(IDescribable, string?, IWords?)"/>. A value no
		///     member has, a <see cref="FlagsAttribute"/> combination or an undefined number,
		///     reads as its own name and number; <see cref="Describable.Members"/> gives a
		///     combination's members one by one.
		/// </summary>
		/// <param name="value">The enum value.</param>
		/// <param name="format">The slots to read, by their letters: <c>G</c> when none.</param>
		/// <param name="words">The words to look the keys up in: <see cref="Words.Known"/> when none.</param>
		[return: Localized]
		public static string Describe(
				this Enum value,
				string? format = null,
				IWords? words = null) {
			Debug.Assert(value != null);
			return Describable.Of(value).Describe(format, words);
		}

		/// <summary>
		///     The engine (runtime SPEC: Describe without the type): each letter of
		///     <paramref name="format"/> reads a slot of <paramref name="value"/>, the key's
		///     words with the slot's suffix, then the text an attribute gave it:
		///     <list type="bullet">
		///         <item><term>G, n</term><description>the key's words, then the general text, then the description, then the name</description></item>
		///         <item><term>N</term><description>the key's words, then the name</description></item>
		///         <item><term>D, d</term><description><c>.desc</c>, then the description</description></item>
		///         <item><term>S</term><description><c>.sub</c>, then the subtitle</description></item>
		///         <item><term>T</term><description><c>.tooltip</c>, then the tooltip</description></item>
		///         <item><term>s</term><description>the symbol's name</description></item>
		///         <item><term>i</term><description>the number, in whatever integer type it has</description></item>
		///     </list>
		///     A slot the app added (<see cref="Describable.Slot"/>) reads its own suffix. A
		///     letter no slot answers reads as <c>G</c> marked <c>#!X#</c>, and warns
		///     (<c>WORDS:SLOT</c>). Text between single quotes is written as it is, <c>''</c>
		///     being a quote, and so is anything that is no letter or digit. A key is looked up
		///     only when a letter asks for it, and once.
		/// </summary>
		/// <param name="value">What to describe.</param>
		/// <param name="format">The slots to read, by their letters: <c>G</c> when none.</param>
		/// <param name="words">The words to look the keys up in: <see cref="Words.Known"/> when none.</param>
		[return: Localized]
		public static string Describe(
				this IDescribable value,
				string? format = null,
				IWords? words = null) {
			ArgumentNullException.ThrowIfNull(value);
			if (string.IsNullOrEmpty(format)) {
				format = "G";
			}
			Dictionary<string, string?>? found = null;
			var sb = new StringBuilder();
			bool quoted = false;
			for (int i = 0; i < format.Length; ++i) {
				char c = format[i];
				if (!quoted) {
					appendCode(c);
				}
				else if (c != '\'') {
					sb.Append(c);
				}
				else if (i + 1 < format.Length && format[i + 1] == '\'') {
					sb.Append(c);
					++i;
				}
				else {
					quoted = false;
				}
			}
			return sb.ToString();

			//the key's words with a suffix, looked up the first time a letter asks
			string? Find(string suffix) {
				if (value.Key is not { } key) {
					return null;
				}
				found ??= [];
				if (!found.TryGetValue(suffix, out string? text)) {
					words ??= Words.Known;
					found[suffix] = words.TryGetValue(key + suffix, out text) ? text : null;
				}
				return text;
			}

			string General() => Find("") ?? value.Text('G') ?? value.Text('D') ?? value.Name;

			void appendCode(char c) {
				switch (c) {
					case '\'':
						quoted = true;
						break;
					case 'G' or 'n':
						sb.Append(General());
						break;
					case 'N':
						sb.Append(Find("") ?? value.Name);
						break;
					case 'D' or 'd':
						sb.Append(Find(".desc") ?? value.Text('D'));
						break;
					case 'S':
						sb.Append(Find(".sub") ?? value.Text('S'));
						break;
					case 'T':
						sb.Append(Find(".tooltip") ?? value.Text('T'));
						break;
					case 's':
						sb.Append(value.Name);
						break;
					case 'i':
						sb.Append(value.Number);
						break;
					case var other when !char.IsLetterOrDigit(other):
						sb.Append(other);
						break;
					case var slot when Describable.SuffixOf(slot) is { } suffix:
						sb.Append(Find(suffix) ?? value.Text(slot));
						break;
					default:
						//a slot nobody added: the general text, marked so it can be searched for
						Words.Logger.Warn($"WORDS:SLOT:`{c}`");
						sb.Append(General()).Append($"#!{c}#");
						break;
				}
			}
		}

		/// <summary>
		///     Each of <paramref name="values"/> described, one text apiece: the members
		///     <see cref="Describable.Members"/> finds in a <see cref="FlagsAttribute"/> value.
		/// </summary>
		/// <param name="values">What to describe.</param>
		/// <param name="format">The slots to read, by their letters: <c>G</c> when none.</param>
		/// <param name="words">The words to look the keys up in: <see cref="Words.Known"/> when none.</param>
		public static IReadOnlyList<string> Describe(
				this IEnumerable<IDescribable> values,
				string? format = null,
				IWords? words = null) {
			ArgumentNullException.ThrowIfNull(values);
			return [.. values.Select(value => value.Describe(format, words))];
		}

		private const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

		/// <summary>
		///     Returns the <see cref="MemberInfo"/> associated with <paramref name="value"/> if it
		///     is defined; otherwise, returns <see langword="null"/>.
		/// </summary>
		public static MemberInfo? GetEnumMemberInfo(this Enum value) {
			Debug.Assert(value != null);
			var type = value.GetType();
			return Enum.GetNames(type)
				.Where(n => Enum.Parse(type, n).Equals(value))
				.Select(n => GetEnumMemberInfo(type, n))
				.OfType<MemberInfo>()
				.OrderBy(m => m.GetCustomAttribute<ObsoleteAttribute>() != null)
				.FirstOrDefault();
		}
		/// <summary>
		///     Returns the <see cref="MemberInfo"/> associated with <paramref name="name"/> from
		///     the <see langword="enum"/> <paramref name="type"/>.
		/// </summary>
		/// <param name="type">The enum type.</param>
		/// <param name="name">The name of the field.</param>
		public static MemberInfo? GetEnumMemberInfo(Type type, string name) {
			Debug.Assert(type != null);
			Debug.Assert(type.IsEnum);
			return type.GetMember(name, MemberTypes.Field, flags)
				.SingleOrDefault();
		}
		/// <summary>
		///     Returns the <see cref="MemberInfo"/> associated with <paramref name="name"/> from
		///     the <see langword="enum"/> <typeparamref name="TEnum"/>.
		/// </summary>
		/// <typeparam name="TEnum">The enum type.</typeparam>
		/// <param name="name">The name of the field.</param>
		public static MemberInfo? GetEnumMemberInfo<TEnum>(string name) where TEnum : Enum {
			return typeof(TEnum)
				.GetMember(name, MemberTypes.Field, flags)
				.SingleOrDefault();
		}
		/// <summary>
		/// Search for the first matching Attribute on the given enum value.
		/// </summary>
		/// <typeparam name="T">The attribute you seek.</typeparam>
		/// <param name="value">The enum value.</param>
		/// <returns>Matching attribute or <see langword="null"/> if not present.</returns>
		public static T? GetEnumMemberAttribute<T>(this Enum value)
			where T : Attribute {
			Debug.Assert(value != null);
			return value.GetEnumMemberInfo()
				?.GetCustomAttribute<T>(false);
		}

		/// <summary>
		/// Execute a Regular Expression and output the <see cref="Match"/>, returning true if successful.
		/// </summary>
		/// <param name="pattern">The Regex pattern.</param>
		/// <param name="subject">The string to search.</param>
		/// <param name="match">The search result, successful or not.</param>
		/// <returns>True if the match succeeded.</returns>
		public static bool TryMatch(this Regex pattern, string subject, out Match match) {
			match = pattern.Match(subject);
			return match.Success;
		}

		/// <summary>
		/// Retrieve and output a named group from a Regex result, returning true if successful.
		/// </summary>
		/// <param name="match">The regex result.</param>
		/// <param name="key">The name of the match group.</param>
		/// <param name="group">The match group if found; or null if not found.</param>
		/// <returns>True if the group was found.</returns>
		public static bool TryGetGroup(this Match match, string key, [NotNullWhen(true)] out Group? group)
			=> match.Groups.TryGetValue(key, out group) && group.Success;

		/// <summary>
		/// Filter a nullable sequence of all null values, informing a null-aware
		/// linter that the sequence is now null-free.
		/// </summary>
		/// <typeparam name="T">The sequence type.</typeparam>
		/// <param name="list">The sequence.</param>
		/// <returns>The sequence without nulls.</returns>
		public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> list) {
			foreach (var item in list) {
				if (item is not null) {
					yield return item;
				}
			}
		}
		/// <summary>
		/// Filter a nullable sequence of all null values, informing a null-aware
		/// linter that the sequence is now null-free.
		/// </summary>
		/// <typeparam name="T">The sequence type.</typeparam>
		/// <param name="list">The sequence.</param>
		/// <returns>The sequence without nulls.</returns>
		public static IEnumerable<T> WhereNotNull<T>(this IList<T?> list) {
			for (int i = 0; i < list.Count; ++i) {
				if (list[i] is { } item) {
					yield return item;
				}
			}
		}

		/// <summary>
		/// Scan a list for a matching item according to the callback and return its index,
		/// or -1 if not found.
		/// </summary>
		/// <typeparam name="T">The type of item in the list.</typeparam>
		/// <param name="list">The list to search.</param>
		/// <param name="match">The comparison callback.</param>
		/// <returns>The index of the matching item; otherwise, -1.</returns>
		public static int FindIndex<T>(this IList<T> list, Predicate<T> match) {
			for (int i = 0; i < list.Count; ++i) {
				if (match(list[i])) {
					return i;
				}
			}
			return -1;
		}
	}
}
