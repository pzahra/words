using PatTech.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace PatTech.Localization {
	/// <summary>
	///     What <see cref="Extensions.Describe(IDescribable, string?, IWords?)"/> reads off
	///     the thing it describes (runtime SPEC: Describe without the type): an enum member,
	///     cached from its type by the dictionary's <see cref="DescribeRegistry"/>, or
	///     anything built to stand for one, such as a key with no type behind it.
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
	///     What <see cref="WordsBuilder.ConfigureDescribe"/> hands its callback to register
	///     with (runtime SPEC: Describe without the type): which attributes give a member its
	///     key or a slot its text, the keys an enum gives its members with none of their own,
	///     and slots of the app's own. Each call adds to the builder's registry and returns
	///     this, to chain the next; once the callback returns, it refuses any more.
	/// </summary>
	public interface IDescribeBuilder {
		/// <summary>
		///     Adds a slot of the app's own: <paramref name="slot"/> reads the key's words with
		///     <paramref name="suffix"/>, <c>Slot('H', ".hint")</c> reading <c>key.hint</c>, then
		///     the text an attribute registered for it gives.
		/// </summary>
		/// <param name="slot">The slot's letter: a letter or digit no slot has.</param>
		/// <param name="suffix">A dot and one segment of a key's name.</param>
		/// <exception cref="ArgumentException">The letter is taken or no letter, or the suffix is no dot and segment.</exception>
		IDescribeBuilder Slot(char slot, string suffix);

		/// <summary>
		///     Registers <typeparamref name="TAttribute"/> as giving the slot
		///     <paramref name="slot"/> its text, read by <paramref name="text"/>, so
		///     <c>Describe</c> reads an attribute an app already has: the general text
		///     (<c>G</c>), the description (<c>d</c>), the subtitle (<c>S</c>), the tooltip
		///     (<c>T</c>), or a slot the app added. An attribute deriving from
		///     <typeparamref name="TAttribute"/> is read the same way, unless a type nearer
		///     its own is registered: an attribute is read as its nearest registered type
		///     alone, so a subclass registered for one slot gives only what it is
		///     registered for. The first registration of <see cref="WordsAttribute"/>,
		///     <see cref="DescriptionAttribute"/> or <see cref="TooltipAttribute"/> replaces
		///     Words' own reading of it, and later ones add to the app's. Where two
		///     attributes give a member the same slot, the one registered first wins.
		/// </summary>
		/// <exception cref="ArgumentException">The slot takes no text from an attribute.</exception>
		IDescribeBuilder Fill<TAttribute>(char slot, Func<TAttribute, string?> text) where TAttribute : Attribute;

		/// <summary>
		///     Registers <typeparamref name="TAttribute"/> as naming a member's key, read by
		///     <paramref name="key"/>, as is an attribute deriving from it, unless a type
		///     nearer its own is registered. An attribute of another system that holds its
		///     key keeps its source: <c>FillKey&lt;ResourceAttribute&gt;(resource =&gt; "legacy." + resource.Name)</c>.
		///     A built-in one registered here is read as the key instead of as Words reads it:
		///     <c>FillKey&lt;DescriptionAttribute&gt;(description =&gt; description.Description)</c>
		///     takes each <c>[Description]</c> for a key and gives no description.
		///     <see cref="WordsAttribute"/> is registered first, so it wins over another attribute.
		/// </summary>
		IDescribeBuilder FillKey<TAttribute>(Func<TAttribute, string?> key) where TAttribute : Attribute;

		/// <summary>
		///     Gives each member of <typeparamref name="TEnum"/> without a key of its own the
		///     key <paramref name="prefix"/>, a dot and its name: <c>Keys&lt;Brew&gt;("enums.brew")</c>
		///     reads <c>Brew.Latte</c> at <c>enums.brew.Latte</c>.
		/// </summary>
		/// <exception cref="ArgumentException">The prefix is no key's name.</exception>
		IDescribeBuilder Keys<TEnum>(string prefix) where TEnum : struct, Enum;

		/// <summary>
		///     Gives each member of <typeparamref name="TEnum"/> without a key of its own the
		///     key <paramref name="key"/> returns for it, or none where it returns
		///     <see langword="null"/>, so an enum one does not own, or one whose keys do not
		///     follow its names, reads words all the same:
		///     <c>Keys&lt;HttpStatusCode&gt;(code => $"http.{(int)code}")</c> reads
		///     <c>HttpStatusCode.NotFound</c> at <c>http.404</c>. Asked once per member, when
		///     the type is first described.
		/// </summary>
		IDescribeBuilder Keys<TEnum>(Func<TEnum, string?> key) where TEnum : struct, Enum;
	}

	/// <summary>
	///     Enum members as <see cref="IDescribable"/>s, read by a dictionary's
	///     <see cref="IWords.DescribeRegistry"/> (runtime SPEC: Describe without the type),
	///     and describables that stand for one.
	/// </summary>
	public static class Describable {
		/// <summary>
		///     The member <paramref name="value"/> names, as <paramref name="words"/>' registry
		///     reads it, or, for a value no member has (a <see cref="FlagsAttribute"/>
		///     combination, an undefined number), a describable of its own name and number,
		///     with no key and no text.
		/// </summary>
		/// <param name="value">The enum value.</param>
		/// <param name="words">The dictionary whose registry reads the member: <see cref="Words.Known"/> when none.</param>
		public static IDescribable Of(Enum value, IWords? words = null) {
			ArgumentNullException.ThrowIfNull(value);
			return (words ?? Words.Known).DescribeRegistry.MembersOf(value.GetType()).TryGetValue(value, out DescribeRegistry.Member? member)
				? member
				: new DescribeRegistry.Member(value.ToString(), value.ToString("D"), null, null);
		}

		/// <summary>
		///     A describable of a key alone, with no type behind it: its name the key's last
		///     segment, no number and no attribute's text, so each slot reads the words
		///     beside the key. Wordsmith's <c>enum</c> input describes a member this way.
		/// </summary>
		public static IDescribable OfKey(string key) {
			ArgumentNullException.ThrowIfNull(key);
			return new DescribeRegistry.Member(key[(key.LastIndexOf('.') + 1)..], "", key, null);
		}

		/// <summary>
		///     The members <paramref name="value"/> is made of, as <see cref="Enum.ToString()"/>
		///     names them, so a <see cref="FlagsAttribute"/> combination settles overlapping
		///     members the way .NET does; any other value is its one describable.
		/// </summary>
		/// <param name="value">The enum value.</param>
		/// <param name="words">The dictionary whose registry reads the members: <see cref="Words.Known"/> when none.</param>
		public static IReadOnlyList<IDescribable> Members(Enum value, IWords? words = null) {
			ArgumentNullException.ThrowIfNull(value);
			words ??= Words.Known;
			Type type = value.GetType();
			string[] names = value.ToString().Split(", ");
			if (names.Length == 1) {
				return [Of(value, words)];
			}
			return [.. names.Select(name => Enum.TryParse(type, name, out object? part) ? Of((Enum)part, words) : new DescribeRegistry.Member(name, "", null, null))];
		}
	}

	/// <summary>
	///     What <c>Describe</c> reads off an enum, frozen into a dictionary (runtime SPEC:
	///     Describe without the type): which attributes give a member its key or a slot its
	///     text, the keys an enum gives its members with none of their own, and the slots an
	///     app added. A <see cref="WordsBuilder"/> makes one of what
	///     <see cref="WordsBuilder.ConfigureDescribe"/> registered, and each dictionary it
	///     produces carries it as <see cref="IWords.DescribeRegistry"/>; nothing registers into
	///     it after. It keeps each enum type's members, built once, for every dictionary
	///     carrying it, and never their words, so a language switched live reads afresh.
	/// </summary>
	public sealed class DescribeRegistry {
		private readonly State state;
		private readonly ConcurrentDictionary<Type, Lazy<Dictionary<Enum, Member>>> cache = new();

		private DescribeRegistry(State state) => this.state = state;

		/// <summary>Words' own readings alone: <c>[Words]</c> for the key, <c>[Description]</c> for <c>d</c>, <c>[Tooltip]</c> for <c>S</c> and <c>T</c>.</summary>
		internal static DescribeRegistry BuiltIn { get; } = new(State.BuiltIn());

		/// <summary>The suffix of a slot an app added, by its letter; <see langword="null"/> for any other letter.</summary>
		internal string? SuffixOf(char slot) => state.Slots.GetValueOrDefault(slot);

		//runs configure on a registrar adding to this registry, closed once it returns,
		//and gives the registry it made: this one where it registered nothing, so the
		//members built stay, and none where it threw
		internal DescribeRegistry Configure(Action<IDescribeBuilder> configure) {
			var registering = new Registering(state);
			try {
				configure(registering);
			}
			finally {
				registering.Close();
			}
			return ReferenceEquals(registering.State, state) ? this : new DescribeRegistry(registering.State);
		}

		//a type is built once, however many threads first describe it together: GetOrAdd
		//may make a Lazy for each, but keeps one, and that one alone builds; a build that
		//threw is not kept, so the next describe tries again
		internal Dictionary<Enum, Member> MembersOf(Type type) {
			Lazy<Dictionary<Enum, Member>> members = cache.GetOrAdd(type, kind => new(() => state.Build(kind), LazyThreadSafetyMode.ExecutionAndPublication));
			try {
				return members.Value;
			}
			catch {
				cache.TryRemove(new(type, members));
				throw;
			}
		}

		private static bool IsBuiltIn(char slot) => slot is 'G' or 'N' or 'd' or 'D' or 'S' or 'T' or 's';

		//what ConfigureDescribe's callback registers with: each call a new state, kept
		//only once the callback returns
		private sealed class Registering(State state) : IDescribeBuilder {
			private bool closed;

			public State State { get; private set; } = state;

			public void Close() => closed = true;

			public IDescribeBuilder Slot(char slot, string suffix) {
				ArgumentNullException.ThrowIfNull(suffix);
				if (!char.IsLetterOrDigit(slot) || IsBuiltIn(slot)) {
					throw new ArgumentException($"'{slot}' is no slot of the app's own to add: a built-in slot's, or no letter", nameof(slot));
				}
				if (!suffix.StartsWith('.') || !WordsParser.IsKeySegment(suffix[1..])) {
					throw new ArgumentException($"'{suffix}' is no suffix: a dot and one segment of a key's name", nameof(suffix));
				}
				return Change(current => {
					if (current.Slots.ContainsKey(slot)) {
						throw new ArgumentException($"'{slot}' is a slot already, reading {current.Slots[slot]}", nameof(slot));
					}
					return current with { Slots = new(current.Slots) { [slot] = suffix } };
				});
			}

			public IDescribeBuilder Fill<TAttribute>(char slot, Func<TAttribute, string?> text) where TAttribute : Attribute {
				ArgumentNullException.ThrowIfNull(text);
				return Change(current => {
					if (slot is not ('G' or 'd' or 'S' or 'T') && !current.Slots.ContainsKey(slot)) {
						throw new ArgumentException($"'{slot}' takes no text from an attribute: G, d, S, T or a slot the app added", nameof(slot));
					}
					return current.Adding(typeof(TAttribute), new Giver(slot, attribute => text((TAttribute)attribute)));
				});
			}

			public IDescribeBuilder FillKey<TAttribute>(Func<TAttribute, string?> key) where TAttribute : Attribute {
				ArgumentNullException.ThrowIfNull(key);
				return Change(current => current.Adding(typeof(TAttribute), new Giver(null, attribute => key((TAttribute)attribute))));
			}

			public IDescribeBuilder Keys<TEnum>(string prefix) where TEnum : struct, Enum {
				ArgumentNullException.ThrowIfNull(prefix);
				if (!WordsParser.IsKeyName(prefix)) {
					throw new ArgumentException($"'{prefix}' is no key's name", nameof(prefix));
				}
				return Change(current => current.Keying(typeof(TEnum), (_, name) => $"{prefix}.{name}"));
			}

			public IDescribeBuilder Keys<TEnum>(Func<TEnum, string?> key) where TEnum : struct, Enum {
				ArgumentNullException.ThrowIfNull(key);
				return Change(current => current.Keying(typeof(TEnum), (value, _) => key((TEnum)value)));
			}

			private Registering Change(Func<State, State> change) {
				if (closed) {
					throw new InvalidOperationException("ConfigureDescribe has returned: register inside its callback");
				}
				State = change(State);
				return this;
			}
		}

		//what an attribute gives: a slot's text, or the key where Slot is null
		private sealed record Giver(char? Slot, Func<Attribute, string?> Read);

		//the key a type gives a member with none of its own, from the value and its name
		private delegate string? KeyOf(Enum value, string name);

		//Defaults: the types still read as Words reads them by default
		private sealed record State(
				Dictionary<Type, List<(int Order, Giver Giver)>> Givers,
				int Registered,
				Dictionary<Type, KeyOf> TypeKeys,
				Dictionary<char, string> Slots,
				HashSet<Type> Defaults) {

			//a type has one way to key its members, the latest given
			public State Keying(Type type, KeyOf key) => this with { TypeKeys = new(TypeKeys) { [type] = key } };

			public static State BuiltIn() {
				var state = new State([], 0, [], [], []);
				state = state.Adding(typeof(WordsAttribute), new Giver(null, attribute => ((WordsAttribute)attribute).Key));
				state = state.Adding(typeof(DescriptionAttribute), new Giver('d', attribute => ((DescriptionAttribute)attribute).Description));
#pragma warning disable CS0618 // the migration aid stays readable for the code that has it
				state = state.Adding(typeof(TooltipAttribute), new Giver('S', attribute => ((TooltipAttribute)attribute).Text));
				state = state.Adding(typeof(TooltipAttribute), new Giver('T', attribute => ((TooltipAttribute)attribute).Text));
#pragma warning restore CS0618
				return state with { Defaults = [.. state.Givers.Keys] };
			}

			//an app's registrations of a type add up, but its first of one Words reads by
			//default replaces that reading, since only the app knows what its attribute
			//holds: [Description] registered with FillKey names the key and gives no d
			public State Adding(Type attribute, Giver giver) {
				var givers = Givers.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
				HashSet<Type> defaults = Defaults;
				if (Defaults.Contains(attribute)) {
					givers.Remove(attribute);
					defaults = [.. Defaults.Where(type => type != attribute)];
				}
				if (!givers.TryGetValue(attribute, out var list)) {
					givers[attribute] = list = [];
				}
				list.Add((Registered, giver));
				return this with { Givers = givers, Registered = Registered + 1, Defaults = defaults };
			}

			//each value's member is its first name that is not obsolete, as Enum.GetNames
			//orders them, or its first; each member's attributes are asked for once, and
			//only those the registry knows are made and give it anything
			public Dictionary<Enum, Member> Build(Type type) {
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
				//the attributes' types read off the metadata, which makes none: one the
				//registry does not know is never made, so one that throws as it is made
				//breaks nothing it has no part in
				foreach (Type kind in field.GetCustomAttributesData().Select(data => data.AttributeType).Distinct()) {
					if (GiversOf(kind) is not { } givers) {
						continue;
					}
					foreach (Attribute attribute in field.GetCustomAttributes(kind, false).Cast<Attribute>().Where(made => made.GetType() == kind)) {
						foreach (var (order, giver) in givers) {
							if (giver.Read(attribute) is not { } text) {
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
				}
				if (key is null && TypeKeys.TryGetValue(type, out KeyOf? keyOf)) {
					key = keyOf(value, name);
				}
				return new Member(name, value.ToString("D"), key, texts?.ToDictionary(pair => pair.Key, pair => pair.Value.Text));
			}

			//an attribute is read as the nearest registered type of its lineage, and that
			//alone: its own where it is registered, since a subclass may hold something
			//else in its base's property (another system's key where [Description] holds
			//text); an unregistered subclassed [Description] reads as [Description]
			private List<(int Order, Giver Giver)>? GiversOf(Type kind) {
				for (Type? at = kind; at is not null; at = at.BaseType) {
					if (Givers.TryGetValue(at, out var givers)) {
						return givers;
					}
				}
				return null;
			}
		}

		internal sealed class Member(string name, string number, string? key, Dictionary<char, string>? texts) : IDescribable {
			public string Name => name;
			public string Number => number;
			public string? Key => key;
			public string? Text(char slot) => texts?.GetValueOrDefault(slot);
		}
	}
}
