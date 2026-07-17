using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PatTech.Localization.Wpf {
	public class MarkdownParser(float baseFontSize = 13, ITakeException? logger = null) : MarkdownParser<Inline>(logger), IMarkdownParser {
		protected override Inline Run(string text) => new Run { Text = text };
		protected override Inline Span(IEnumerable<Inline> inlines) {
			var span = new Span();
			span.Inlines.AddRange(inlines);
			return span;
		}

		protected override Inline Hyperlink(Inline content, Uri target, string? tooltip) {
			content.TextDecorations = TextDecorations.Underline;
			content.Foreground = Brushes.Blue;
			return new Hyperlink {
				Inlines = { content },
				NavigateUri = target,
				ToolTip = tooltip,
			};
		}
		protected override void Embolden(ref Inline content) => content.FontWeight = FontWeights.Bold;
		protected override void Italicize(ref Inline content) => content.FontStyle = FontStyles.Italic;
		protected override void Subscript(ref Inline content) {
			content.BaselineAlignment = BaselineAlignment.Subscript;
			content.FontSize = baseFontSize * 0.8f;
			content = Span([content]);
		}
		protected override void Superscript(ref Inline content) {
			content.BaselineAlignment = BaselineAlignment.Superscript;
			content.FontSize = baseFontSize * 0.8f;
			content = Span([content]);
		}
	}

	public interface IMarkdownParser : IMarkdownParser<Inline> { }
}
