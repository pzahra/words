using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PatTech.Localization {
	public static partial class Words {
		//one rendering of words, written as it goes (SPEC: One pass for every brace): the
		//provider its keys are read from, the arguments its selectors count by, and what its
		//format items are written as, filled or numbered for string.Format. Each value is
		//read into its pieces once, and what a reference or a selector brings in is rendered
		//as pieces of its own, never re-read with the text around it
		private sealed class Rendering(IWordsProvider provider, Selecting? selecting, Items? items) {
			private readonly StringBuilder written = new();
			//the entries being rendered, for the circular cut
			private readonly Stack<string> path = new();

			public override string ToString() => written.ToString();

			//entry's words, rendered against the key they are words of, however the entry
			//was reached
			public void Key(string entry) {
				if (path.Contains(entry)) {
					Logger.Warn($"WORDS:CIRC:`{entry}` <- `{string.Join("` <- `", path)}`");
					Text("# ∞ #");
					return;
				}
				if (entry.StartsWith('$')) {
					if (provider.TryGetValue(entry, out var constant)) {
						Text(constant);
					}
					else {
						Logger.Warn($"WORDS:CONST:`{entry}`");
						Text($"#{entry}#");
					}
					return;
				}
				if (!provider.TryGetValue(entry, out var value)) {
					Logger.Warn($"WORDS:KEY:`{entry}`");
					Text($"#{entry}#");
					return;
				}
				path.Push(entry);
				try {
					Pieces(value, KeyOf(entry), WordsTemplate.Parse(value));
				}
				finally {
					path.Pop();
				}
			}

			//entry's words from the provider, so each reference and selector resolves against
			//the key it was written in; an entry the provider lacks reads through the indexer,
			//as an IWords of one's own may answer it
			public void Entry(IWords words, string entry) {
				if (provider.ContainsKey(entry)) {
					Key(entry);
				}
				else {
					Answer(words[entry], entry);
				}
			}

			//a caller's text, which is no key's words: baseKey anchors its relatives and
			//nothing else, so it may refer to baseKey and select among its forms
			public void Value(string text, string? baseKey) => Pieces(text, baseKey, WordsTemplate.Parse(text));

			//a template no dictionary holds, which refers to nothing
			public void Template(string template) => Pieces(template, null, WordsTemplate.ParseFormat(template));

			//words an indexer handed over are rendered already, so only their selectors and
			//items are left, each against entry's key; entry is on the path, as the provider's
			//words would be, so a selector reaching back to its own key is cut. With neither
			//to fill, they stand as they are
			private void Answer(string words, string entry) {
				if (selecting is null && items is null) {
					written.Append(words);
					return;
				}
				path.Push(entry);
				try {
					Pieces(words, KeyOf(entry), WordsTemplate.ParseFormat(words));
				}
				finally {
					path.Pop();
				}
			}

			//text's pieces, each against key. A selector with no language to select in, and
			//an item with nothing to fill it, stay as written
			private void Pieces(string text, string? key, IReadOnlyList<WordsPiece> pieces) {
				foreach (WordsPiece piece in pieces) {
					switch (piece) {
						case WordsPiece.Text run:
							Text(run.Value);
							break;
						case WordsPiece.Reference reference:
							Key(Relative(reference.Key, key));
							break;
						case WordsPiece.Constant constant:
							Key(constant.Key);
							break;
						case WordsPiece.Selector selector when selecting is not null:
							Select(selecting, selector.Argument, Relative(selector.Key, key));
							break;
						case WordsPiece.Item item when items is not null:
							items.Write(written, item, piece.In(text));
							break;
						case WordsPiece.Malformed:
							Logger.Warn($"WORDS:FORMAT:`{piece.In(text)}`");
							Text(piece.In(text));
							break;
						default:
							Text(piece.In(text));
							break;
					}
				}
			}

			//the form of key its argument selects, rendered against key and selected through
			//in turn: a key whose words are being rendered, plain or a form, is out of reach
			//of its own selectors, as a key is of its own references
			private void Select(Selecting counting, string argument, string key) {
				if (path.Any(entry => entry == key || entry.StartsWith(key + "#", StringComparison.Ordinal))) {
					Logger.Warn($"WORDS:CIRC:`{key}` <- `{string.Join("` <- `", path)}`");
					Text("# ∞ #");
					return;
				}
				Key(FormKey(provider, counting.Language, key, Category(counting.Language, counting.Values.Read(argument))));
			}

			private void Text(string text) {
				if (items is null) {
					written.Append(text);
				}
				else {
					items.Text(written, text);
				}
			}
		}

		//the arguments a call's selectors and format items read: by number, a positional
		//argument, and on the named path the one past them is the value itself,
		//PreFormatByName's slot; by name, on the named path, a member of the value as
		//FormatByName reads it, and on the positional path none
		private sealed class Arguments(object?[]? args, bool named, object? value = null) {
			private readonly int count = args?.Length ?? 0;

			public bool TryRead(string argument, out object? found) {
				if (char.IsAsciiDigit(argument[0])) {
					if (int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out int index)) {
						if (index < count) {
							found = args![index];
							return true;
						}
						if (named && index == count) {
							found = value;
							return true;
						}
					}
				}
				else if (named) {
					return TryMember(value, argument, out found);
				}
				found = null;
				return false;
			}

			//what a selector counts: an argument there is none of warns, and is nothing
			public object? Read(string argument) {
				if (TryRead(argument, out var found)) {
					return found;
				}
				Logger.Warn($"WORDS:FIELD:`{argument}`");
				return null;
			}
		}

		//what a rendering writes its format items, and the text around them, as
		private abstract class Items {
			public virtual void Text(StringBuilder written, string text) => written.Append(text);

			public abstract void Write(StringBuilder written, WordsPiece.Item item, string asWritten);

			//an item with nothing to read collapses to its argument, marked, and warns
			protected static string Unread(string argument) {
				Logger.Warn($"WORDS:FIELD:`{argument}`");
				return Marked(argument);
			}

			protected static string Marked(string argument) => $"#{{{argument}}}#";
		}

		//items filled as string.Format fills them, padded to the alignment: the provider's
		//ICustomFormatter first, handed the argument as it is, then, where it returns null,
		//the describer, which describes an enum or a describable in the dictionary, the
		//spec being Describe's letters, and formats anything else by the provider. A spec
		//the argument rejects marks the item, as an argument there is none of does
		private sealed class Filling(Arguments arguments, IFormatProvider? provider, IWords? dictionary) : Items {
			private readonly ICustomFormatter? custom = provider?.GetFormat(typeof(ICustomFormatter)) as ICustomFormatter;
			//no dictionary describes in Known, read once for every argument
			private readonly Describer describer = new(dictionary ?? Known, provider);

			public override void Write(StringBuilder written, WordsPiece.Item item, string asWritten) {
				if (!arguments.TryRead(item.Argument, out object? value)) {
					written.Append(Unread(item.Argument));
					return;
				}
				string text;
				try {
					text = custom?.Format(item.Format, value, provider) ?? describer.Format(item.Format, value, provider);
				}
				catch (FormatException) {
					Logger.Warn($"WORDS:FORMAT:`{asWritten}`");
					written.Append(Marked(item.Argument));
					return;
				}
				int padding = Math.Abs(item.Alignment ?? 0) - text.Length;
				if (padding > 0 && item.Alignment > 0) {
					written.Append(' ', padding);
				}
				written.Append(text);
				if (padding > 0 && item.Alignment < 0) {
					written.Append(' ', padding);
				}
			}
		}

		//items written for string.Format, numbered, and the arguments they read, as
		//PreFormatByName hands them over: the positional arguments, the value itself, then a
		//slot per name as it first appears. The text is escaped as string.Format wants it
		private sealed class Numbering(object?[]? args, object? value) : Items {
			private readonly Arguments arguments = new(args, named: true, value);
			private readonly int count = args?.Length ?? 0;
			private readonly List<object?> slots = [.. args ?? [], value];
			private readonly List<string> names = [];

			public object?[] Slots => [.. slots];

			public override void Text(StringBuilder written, string text) {
				if (text.AsSpan().IndexOfAny('{', '}') < 0) {
					written.Append(text);
				}
				else {
					written.Append(text.Replace("{", "{{").Replace("}", "}}"));
				}
			}

			public override void Write(StringBuilder written, WordsPiece.Item item, string asWritten) {
				if (Number(item.Argument) is not { } number) {
					Text(written, Unread(item.Argument));
					return;
				}
				written.Append('{').Append(number);
				if (item.Alignment is int alignment) {
					written.Append(',').Append(alignment.ToString(CultureInfo.InvariantCulture));
				}
				if (item.Format is { } format) {
					written.Append(':').Append(format);
				}
				written.Append('}');
			}

			//the number an argument is written as; none for one there is none of
			private string? Number(string argument) {
				if (char.IsAsciiDigit(argument[0])) {
					return arguments.TryRead(argument, out _) ? argument : null;
				}
				int slot = names.IndexOf(argument);
				if (slot < 0 && arguments.TryRead(argument, out object? found)) {
					slot = names.Count;
					names.Add(argument);
					slots.Add(found);
				}
				return slot < 0 ? null : (count + 1 + slot).ToString(CultureInfo.InvariantCulture);
			}
		}
	}
}
