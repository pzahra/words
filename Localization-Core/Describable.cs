using PatTech.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace PatTech.Localization {
	/// <summary>
	///     What <see cref="Extensions.Describe(IDescribable, string?, IWords?)"/> reads off
	///     the thing it describes (runtime SPEC: Describe without the type): an enum member,
	///     cached from its type by <see cref="Describable"/>, or anything built to stand for
	///     one, such as a key with no type behind it.
	/// </summary>
	public interface IDescribable {
		/// <summary>The symbol's name: format <c>s</c>, and the last resort of <c>G</c> and <c>N</c>.</summary>
		string Name { get; }
		/// <summary>The value as a number, in whatever integer type it has: format <c>D</c>. Empty where there is none.</summary>
		string Number { get; }
		/// <summary>The words key, read for <c>G</c> and, with each slot's suffix, for the slot; <see langword="null"/> where there is none.</summary>
		string? Key { get; }
		/// <summary>
		///     The text an attribute gives the slot <paramref name="slot"/>, which the slot
		///     falls back to where the key has no words; <see langword="null"/> where none does.
		/// </summary>
		/// <param name="slot">The slot's letter, as the format names it.</param>
		string? Text(char slot);
	}

	/// <summary>
	///     The built-in slots, named by their format letters; cast to <see cref="char"/> where
	///     a slot is asked for, so <c>(char)DescribeSlot.Tooltip</c> and <c>'T'</c> are one slot.
	/// </summary>
	public enum DescribeSlot : byte {
		/// <summary><c>G</c>: the key's words, then the general text an attribute gives, then the description, then the name.</summary>
		General = (byte)'G',
		/// <summary><c>N</c>: the key's words, then the name.</summary>
		Name = (byte)'N',
		/// <summary><c>d</c>: the key's <c>.desc</c>, then the description an attribute gives.</summary>
		Description = (byte)'d',
		/// <summary><c>S</c>: the key's <c>.sub</c>, then the subtitle an attribute gives.</summary>
		Subtitle = (byte)'S',
		/// <summary><c>T</c>: the key's <c>.tooltip</c>, then the tooltip an attribute gives.</summary>
		Tooltip = (byte)'T',
		/// <summary><c>s</c>: the symbol's name.</summary>
		Symbol = (byte)'s',
		/// <summary><c>D</c>: the number, as <see cref="Enum.ToString(string)"/> formats it for <c>D</c>.</summary>
		Number = (byte)'D',
	}

	/// <summary>
	///     Enum members as <see cref="IDescribable"/>s, built once per member and kept per
	///     type, and the registry they are built from (runtime SPEC: Describe without the
	///     type): which attributes give a member its key or a slot its text, the keys an
	///     enum gives its members with none of their own, and the slots an app adds.
	///     Register at startup; a registration clears the cache, so a type described
	///     before reads again.
	/// </summary>
	public static class Describable {
		//a registration is a new state, never a change to the one a describe is reading,
		//so a cache built against an old registry dies with it
		private static volatile State state = State.BuiltIn();
		private static readonly object gate = new();

		/// <summary>The suffix of a slot an app added, by its letter; <see langword="null"/> for any other letter.</summary>
		internal static string? SuffixOf(char slot) => state.Slots.GetValueOrDefault(slot);

		/// <summary>
		///     The member <paramref name="value"/> names, or, for a value no member has (a
		///     <see cref="FlagsAttribute"/> combination, an undefined number), a describable
		///     of its own name and number, with no key and no text.
		/// </summary>
		public static IDescribable Of(Enum value) {
			ArgumentNullException.ThrowIfNull(value);
			State current = state;
			return current.MembersOf(value.GetType()).TryGetValue(value, out Member? member)
				? member
				: new Member(value.ToString(), value.ToString("D"), null, null);
		}

		/// <summary>
		///     A describable of a key alone, with no type behind it: its name the key's last
		///     segment, no number and no attribute's text, so each slot reads the words
		///     beside the key. Wordsmith's <c>enum</c> input describes a member this way.
		/// </summary>
		public static IDescribable OfKey(string key) {
			ArgumentNullException.ThrowIfNull(key);
			return new Member(key[(key.LastIndexOf('.') + 1)..], "", key, null);
		}

		/// <summary>
		///     The members <paramref name="value"/> is made of, as <see cref="Enum.ToString()"/>
		///     names them, so a <see cref="FlagsAttribute"/> combination settles overlapping
		///     members the way .NET does; any other value is its one describable.
		/// </summary>
		public static IReadOnlyList<IDescribable> Members(Enum value) {
			ArgumentNullException.ThrowIfNull(value);
			Type type = value.GetType();
			string[] names = value.ToString().Split(", ");
			if (names.Length == 1) {
				return [Of(value)];
			}
			return [.. names.Select(name => Enum.TryParse(type, name, out object? part) ? Of((Enum)part) : new Member(name, "", null, null))];
		}

		/// <summary>
		///     Adds a slot of the app's own: <paramref name="slot"/> reads the key's words with
		///     <paramref name="suffix"/>, <c>Slot('H', ".hint")</c> reading <c>key.hint</c>, then
		///     the text an attribute registered for it gives.
		/// </summary>
		/// <param name="slot">The slot's letter: a letter or digit no slot has.</param>
		/// <param name="suffix">A dot and one segment of a key's name.</param>
		/// <exception cref="ArgumentException">The letter is taken or no letter, or the suffix is no dot and segment.</exception>
		public static void Slot(char slot, string suffix) {
			ArgumentNullException.ThrowIfNull(suffix);
			if (!char.IsLetterOrDigit(slot) || IsBuiltIn(slot)) {
				throw new ArgumentException($"'{slot}' is no slot of the app's own to add: a built-in slot's, or no letter", nameof(slot));
			}
			if (!suffix.StartsWith('.') || !WordsParser.IsKeySegment(suffix[1..])) {
				throw new ArgumentException($"'{suffix}' is no suffix: a dot and one segment of a key's name", nameof(suffix));
			}
			Register(current => {
				if (current.Slots.ContainsKey(slot)) {
					throw new ArgumentException($"'{slot}' is a slot already, reading {current.Slots[slot]}", nameof(slot));
				}
				return current with { Slots = new(current.Slots) { [slot] = suffix } };
			});
		}

		/// <summary>
		///     Registers <typeparamref name="TAttribute"/> as giving the slot
		///     <paramref name="slot"/> its text, read by <paramref name="text"/>, so
		///     <c>Describe</c> reads an attribute an app already has: the general text
		///     (<c>G</c>), the description (<c>d</c>), the subtitle (<c>S</c>), the tooltip
		///     (<c>T</c>), or a slot the app added. Where two attributes give a member the
		///     same slot, the one registered first wins.
		/// </summary>
		/// <exception cref="ArgumentException">The slot takes no text from an attribute.</exception>
		public static void Fill<TAttribute>(char slot, Func<TAttribute, string?> text) where TAttribute : Attribute {
			ArgumentNullException.ThrowIfNull(text);
			Register(current => {
				if (slot is not ('G' or 'd' or 'S' or 'T') && !current.Slots.ContainsKey(slot)) {
					throw new ArgumentException($"'{slot}' takes no text from an attribute: G, d, S, T or a slot the app added", nameof(slot));
				}
				return current.Adding(typeof(TAttribute), new Giver(slot, attribute => text((TAttribute)attribute)));
			});
		}

		/// <summary>
		///     Registers <typeparamref name="TAttribute"/> as naming a member's key, read by
		///     <paramref name="key"/>. <see cref="WordsAttribute"/> is registered first, so it wins.
		/// </summary>
		public static void FillKey<TAttribute>(Func<TAttribute, string?> key) where TAttribute : Attribute {
			ArgumentNullException.ThrowIfNull(key);
			Register(current => current.Adding(typeof(TAttribute), new Giver(null, attribute => key((TAttribute)attribute))));
		}

		/// <summary>
		///     Gives each member of <typeparamref name="TEnum"/> without a key of its own the
		///     key <paramref name="prefix"/>, a dot and its name: <c>Keys&lt;Brew&gt;("enums.brew")</c>
		///     reads <c>Brew.Latte</c> at <c>enums.brew.Latte</c>.
		/// </summary>
		/// <exception cref="ArgumentException">The prefix is no key's name.</exception>
		public static void Keys<TEnum>(string prefix) where TEnum : struct, Enum {
			ArgumentNullException.ThrowIfNull(prefix);
			if (!WordsParser.IsKeyName(prefix)) {
				throw new ArgumentException($"'{prefix}' is no key's name", nameof(prefix));
			}
			Register(current => current.Keying(typeof(TEnum), (_, name) => $"{prefix}.{name}"));
		}

		/// <summary>
		///     Gives each member of <typeparamref name="TEnum"/> without a key of its own the
		///     key <paramref name="key"/> returns for it, or none where it returns
		///     <see langword="null"/>, so an enum one does not own, or one whose keys do not
		///     follow its names, reads words all the same:
		///     <c>Keys&lt;HttpStatusCode&gt;(code => $"http.{(int)code}")</c> reads
		///     <c>HttpStatusCode.NotFound</c> at <c>http.404</c>. Asked once per member, when
		///     the type is first described.
		/// </summary>
		public static void Keys<TEnum>(Func<TEnum, string?> key) where TEnum : struct, Enum {
			ArgumentNullException.ThrowIfNull(key);
			Register(current => current.Keying(typeof(TEnum), (value, _) => key((TEnum)value)));
		}

		internal static bool IsBuiltIn(char slot) => slot is 'G' or 'N' or 'd' or 'D' or 'S' or 'T' or 's';

		private static void Register(Func<State, State> change) {
			lock (gate) {
				state = change(state) with { Cache = new() };
			}
		}

		//what an attribute gives: a slot's text, or the key where Slot is null
		private sealed record Giver(char? Slot, Func<Attribute, string?> Read);

		//the key a type gives a member with none of its own, from the value and its name
		private delegate string? KeyOf(Enum value, string name);

		private sealed record State(
				Dictionary<Type, List<(int Order, Giver Giver)>> Givers,
				int Registered,
				Dictionary<Type, KeyOf> TypeKeys,
				Dictionary<char, string> Slots,
				ConcurrentDictionary<Type, Dictionary<Enum, Member>> Cache) {

			//a type has one way to key its members, the latest given
			public State Keying(Type type, KeyOf key) => this with { TypeKeys = new(TypeKeys) { [type] = key } };

			public static State BuiltIn() {
				var state = new State([], 0, [], [], new());
				state = state.Adding(typeof(WordsAttribute), new Giver(null, attribute => ((WordsAttribute)attribute).Key));
				state = state.Adding(typeof(DescriptionAttribute), new Giver('d', attribute => ((DescriptionAttribute)attribute).Description));
#pragma warning disable CS0618 // the migration aid stays readable for the code that has it
				state = state.Adding(typeof(TooltipAttribute), new Giver('S', attribute => ((TooltipAttribute)attribute).Text));
				state = state.Adding(typeof(TooltipAttribute), new Giver('T', attribute => ((TooltipAttribute)attribute).Text));
#pragma warning restore CS0618
				return state;
			}

			public State Adding(Type attribute, Giver giver) {
				var givers = Givers.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
				if (!givers.TryGetValue(attribute, out var list)) {
					givers[attribute] = list = [];
				}
				list.Add((Registered, giver));
				return this with { Givers = givers, Registered = Registered + 1 };
			}

			public Dictionary<Enum, Member> MembersOf(Type type) => Cache.GetOrAdd(type, Build);

			//each value's member is its first name that is not obsolete, as Enum.GetNames
			//orders them, or its first; each member's attributes are asked for once, and
			//only those the registry knows give it anything
			private Dictionary<Enum, Member> Build(Type type) {
				var chosen = new Dictionary<Enum, (string Name, FieldInfo Field, bool Obsolete)>();
				foreach (string name in Enum.GetNames(type)) {
					if (type.GetField(name, BindingFlags.Public | BindingFlags.Static) is not { } field || field.GetValue(null) is not Enum value) {
						continue;
					}
					bool obsolete = field.IsDefined(typeof(ObsoleteAttribute), false);
					if (!chosen.TryGetValue(value, out var held) || (held.Obsolete && !obsolete)) {
						chosen[value] = (name, field, obsolete);
					}
				}
				return chosen.ToDictionary(pair => pair.Key, pair => Read(type, pair.Value.Name, pair.Key, pair.Value.Field));
			}

			private Member Read(Type type, string name, Enum value, FieldInfo field) {
				string? key = null;
				int keyOrder = int.MaxValue;
				Dictionary<char, (int Order, string Text)>? texts = null;
				foreach (object attribute in field.GetCustomAttributes(false)) {
					if (!Givers.TryGetValue(attribute.GetType(), out var givers)) {
						continue;
					}
					foreach (var (order, giver) in givers) {
						if (giver.Read((Attribute)attribute) is not { } text) {
							continue;
						}
						if (giver.Slot is not { } slot) {
							if (order < keyOrder) {
								(key, keyOrder) = (text, order);
							}
						}
						else if (texts is null || !texts.TryGetValue(slot, out var held) || order < held.Order) {
							(texts ??= [])[slot] = (order, text);
						}
					}
				}
				if (key is null && TypeKeys.TryGetValue(type, out KeyOf? keyOf)) {
					key = keyOf(value, name);
				}
				return new Member(name, value.ToString("D"), key, texts?.ToDictionary(pair => pair.Key, pair => pair.Value.Text));
			}
		}

		private sealed class Member(string name, string number, string? key, Dictionary<char, string>? texts) : IDescribable {
			public string Name => name;
			public string Number => number;
			public string? Key => key;
			public string? Text(char slot) => texts?.GetValueOrDefault(slot);
		}
	}
}
