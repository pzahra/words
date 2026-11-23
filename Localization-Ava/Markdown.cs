using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

namespace PatTech.Localization.Avalonia;

/// <summary>
///     Markdown that is a string already, built by the app rather than looked up by a key, into
///     a <see cref="TextBlock"/> of the app's own (runtime SPEC: Markdown into a TextBlock of
///     one's own): <c>&lt;TextBlock TextWrapping="Wrap" l:Markdown.Text="{Binding Page}"/&gt;</c>.
///     The markdown fills the TextBlock's own inlines, rendered by
///     <see cref="MarkdownParser.Default"/> as <see cref="WordsInline"/> renders it, so the
///     TextBlock keeps its wrapping, trimming, font and style, and a link in it navigates as any
///     rendered link does (<see cref="Hyperlink.RegisterGlobalNavigateHandler"/>).
/// </summary>
/// <remarks>
///     A new value renders again; null or empty leaves the TextBlock empty, and one the parser
///     throws on is told to <see cref="Words.Logger"/> and shown as written. It renders what it is
///     given: words that follow a live language switch come from a <see cref="WordsInline"/>,
///     whose key tells the switch it is due, or from a binding that builds the markdown from
///     <see cref="Words.Known"/> and updates on the switch.
/// </remarks>
public static class Markdown {
	/// <summary>Identifies the <c>Markdown.Text</c> attached property.</summary>
	public static readonly AttachedProperty<string?> TextProperty =
		AvaloniaProperty.RegisterAttached<TextBlock, string?>("Text", typeof(Markdown));

	static Markdown() {
		TextProperty.Changed.AddClassHandler<TextBlock>((textBlock, e) => Render(textBlock, e.NewValue as string));
	}

	/// <summary>The markdown <paramref name="element"/> shows.</summary>
	public static string? GetText(TextBlock element) => element.GetValue(TextProperty);

	/// <summary>Shows <paramref name="value"/>, markdown, in <paramref name="element"/>.</summary>
	public static void SetText(TextBlock element, string? value) => element.SetValue(TextProperty, value);

	//built whole, then swapped in, as WordsInline does
	private static void Render(TextBlock textBlock, string? text) {
		List<Inline> built = Inlines(text);
		textBlock.Inlines ??= new InlineCollection();
		textBlock.Inlines.Clear();
		textBlock.Inlines.AddRange(built);
	}

	//the markdown's inlines; one that will not render is said and shown as written, so the
	//TextBlock always shows the value it holds
	private static List<Inline> Inlines(string? text) {
		if (text is not { Length: > 0 }) {
			return [];
		}
		try {
			return [.. MarkdownParser.Default.ToInlines(text)];
		}
		catch (Exception ex) {
			Words.Logger.Error(ex, "MD:TEXT");
			return [new Run(text)];
		}
	}
}
