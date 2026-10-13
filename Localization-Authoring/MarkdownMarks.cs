using PatTech.Localization;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     What the app's markdown makes of a text, as far as a translation must keep
	///     it (editor SPEC: Machine translation → Nothing is written unchecked): its
	///     code spans, as their text comes through, and its links' targets, an image's
	///     source among them, each in order. The runtime's own parser reads them, so
	///     a check sees what the app will: a span fenced by two backticks holds one, a
	///     link may carry a title, and a target that is no absolute URL is no link,
	///     since the app leaves its label unlinked.
	/// </summary>
	internal sealed class MarkdownMarks : MarkdownParser<MarkdownMarks.Marks> {
		/// <summary>A text's code spans and link targets, in order.</summary>
		public sealed record Marks(IReadOnlyList<string> Codes, IReadOnlyList<string> Links);

		private static readonly Marks none = new([], []);
		private static readonly MarkdownMarks parser = new();

		private MarkdownMarks() : base(null) { }

		/// <summary>The marks of <paramref name="text"/>; null where the app's markdown cannot read it, as for a link inside a link's label.</summary>
		public static Marks? Read(string text) {
			try {
				return parser.ToInline(text);
			}
			catch (InvalidOperationException) {
				return null;
			}
		}

		/// <summary><paramref name="code"/> as markdown writes it, fenced by more backticks than it holds in a row.</summary>
		public static string Fenced(string code) {
			int run = 0, longest = 0;
			foreach (char c in code) {
				run = c == '`' ? run + 1 : 0;
				longest = Math.Max(longest, run);
			}
			string fence = new('`', longest + 1);
			return code.StartsWith('`') || code.EndsWith('`') ? $"{fence} {code} {fence}" : $"{fence}{code}{fence}";
		}

		protected override Marks Span(IEnumerable<Marks> inlines) {
			List<string> codes = [], links = [];
			foreach (var marks in inlines) {
				codes.AddRange(marks.Codes);
				links.AddRange(marks.Links);
			}
			return new(codes, links);
		}

		protected override Marks Run(string text) => none;

		protected override Marks Code(string text) => new([text], []);

		protected override Marks Hyperlink(Marks content, Uri target, string? tooltip) => new(content.Codes, [.. content.Links, target.OriginalString]);

		protected override Marks Image(Uri source, string? altText, string? tooltip) => new([], [source.OriginalString]);

		protected override void Embolden(ref Marks content) { }

		protected override void Italicize(ref Marks content) { }

		protected override void Subscript(ref Marks content) { }

		protected override void Superscript(ref Marks content) { }
	}
}
