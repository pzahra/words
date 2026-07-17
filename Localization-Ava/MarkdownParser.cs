using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace PatTech.Localization.Ava;

public class MarkdownParser(float baseFontSize = 13, ITakeException? logger = null) : MarkdownParser<Inline>(logger), IMarkdownParser {
	protected override Inline Run(string text) => new Run { Text = text };
	protected override Inline Span(IEnumerable<Inline> inlines) => new Span { Inlines = [..inlines] };
	protected override Inline Hyperlink(Inline content, Uri target, string? tooltip) {
		content.TextDecorations = TextDecorations.Underline;
		content.Foreground = Brushes.Blue;
		return new Hyperlink {
			Inlines = [content],
			Uri = target,
			ToolTip = tooltip,
		};
	}
	protected override void Embolden(ref Inline content) => content.FontWeight = FontWeight.Bold;
	protected override void Italicize(ref Inline content) => content.FontStyle = FontStyle.Italic;
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