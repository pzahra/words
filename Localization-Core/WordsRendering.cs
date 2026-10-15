using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PatTech.Localization {
	public static partial class Words {
		//one rendering of words, written as it goes (SPEC: One pass for every brace): the
		//provider its keys are read from, the arguments its selectors count by, and, when
		//formatting, the numbering its format items are written with for string.Format.
		//Each value is read into its pieces once, and what a reference or a selector brings
		//in is rendered as pieces of its own, never re-read with the text around it
		private sealed class Rendering(IWordsProvider provider, Selecting? selecting, Numbering? numbering) {
			private readonly StringBuilder written = new();
			//the entries being rendered, for the circular cut
			private readonly Stack<string> path = new();

			//the text; when formatting, a template for string.Format, the text escaped in it
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
				if (selecting is null && numbering is null) {
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
						case WordsPiece.Item item when numbering is not null:
							Item(numbering, item);
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
				Key(FormKey(provider, counting.Language, key, Category(counting.Language, counting.Argument(argument))));
			}

			//an item as string.Format reads one, by its number; one with nothing to read
			//collapses to its argument, marked, its alignment and spec dropped
			private void Item(Numbering items, WordsPiece.Item item) {
				if (items.Number(item.Argument) is not { } number) {
					Text($"#{{{item.Argument}}}#");
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

			//text as it stands, its braces escaped in a template
			private void Text(string text) {
				if (numbering is null || text.AsSpan().IndexOfAny('{', '}') < 0) {
					written.Append(text);
				}
				else {
					written.Append(text.Replace("{", "{{").Replace("}", "}}"));
				}
			}
		}

		//a rendering's format items numbered for string.Format, and the arguments they
		//read: a number keeps its argument, and on the named path the one past them is the
		//value itself, PreFormatByName's slot; a name takes a slot past those, one per name
		//as it first appears, read as FormatByName reads it
		private sealed class Numbering(object?[]? args, bool named, object? value = null) {
			private readonly int count = args?.Length ?? 0;
			private readonly List<object?> arguments = named ? [.. args ?? [], value] : [.. args ?? []];
			private readonly List<string> names = [];

			public object?[] Arguments => [.. arguments];

			//the number argument is written as, or null for one with nothing to read, warned
			//about: a number past the arguments, a name on the positional path, or a name no
			//member answers
			public string? Number(string argument) {
				if (char.IsAsciiDigit(argument[0])) {
					if (int.Parse(argument, NumberStyles.None, CultureInfo.InvariantCulture) < count + (named ? 1 : 0)) {
						return argument;
					}
				}
				else if (named) {
					int slot = names.IndexOf(argument);
					if (slot < 0 && TryMember(value, argument, out var found)) {
						slot = names.Count;
						names.Add(argument);
						arguments.Add(found);
					}
					if (slot >= 0) {
						return (count + 1 + slot).ToString(CultureInfo.InvariantCulture);
					}
				}
				Logger.Warn($"WORDS:FIELD:`{argument}`");
				return null;
			}
		}
	}
}
