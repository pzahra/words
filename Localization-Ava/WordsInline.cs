using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Metadata;
using System.Collections.Generic;
using System.Globalization;

namespace PatTech.Localization.Avalonia;

/// <summary>
///     An inline that renders the Words for <see cref="Key"/> — markdown and all — inside a
///     <see cref="global::Avalonia.Controls.TextBlock"/> or other flow content.
/// </summary>
/// <remarks>
///     The resolved text may contain format placeholders, filled from <see cref="Params"/>:
///     an array supplies positional <c>{0}</c>-style arguments, while any other single object
///     supplies <c>{Name}</c>-style placeholders looked up by field or property name (see
///     <see cref="Words.FormatByName(string, object?, object?[])"/>). Placeholders format
///     with the thread's <see cref="CultureInfo.CurrentCulture"/> — the formatting culture
///     <c>Digest</c> installed, the same one a plain <c>Words.Format</c> uses. The rendered
///     inlines are rebuilt whenever <see cref="Key"/> or <see cref="Params"/> changes.
/// </remarks>
public class WordsInline : Span {
	/// <summary>Identifies the <see cref="Key"/> styled property.</summary>
	public static readonly StyledProperty<string?> KeyProperty =
		AvaloniaProperty.Register<WordsInline, string?>(nameof(Key));

	/// <summary>Identifies the <see cref="Params"/> styled property.</summary>
	public static readonly StyledProperty<object?> ParamsProperty =
		AvaloniaProperty.Register<WordsInline, object?>(nameof(Params));

	/// <summary>
	///     The key of the Words to render. A null or empty key renders nothing;
	///     an unknown key renders as <c>#key#</c>.
	/// </summary>
	public string? Key {
		get => GetValue(KeyProperty);
		set => SetValue(KeyProperty, value);
	}

	/// <summary>
	///     Optional arguments for the resolved text's format placeholders. An array fills
	///     positional <c>{0}</c> placeholders; any other object fills <c>{Name}</c>
	///     placeholders from its public fields and properties. This is the content property,
	///     so a single argument can be written as the element's content in XAML.
	/// </summary>
	[Content]
	public object? Params {
		get => GetValue(ParamsProperty);
		set => SetValue(ParamsProperty, value);
	}

	/// <inheritdoc/>
	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
		base.OnPropertyChanged(change);

		if (change.Property == KeyProperty) {
			UpdateChild((string?)change.NewValue, Params);
		}
		else if (change.Property == ParamsProperty) {
			UpdateChild(Key, change.NewValue);
		}
	}

	private void UpdateChild(string? key, object? @params) {
		if (string.IsNullOrEmpty(key)) {
			Inlines.Clear();
			return;
		}

		var text = Words.Known.FormatParams(key, @params, CultureInfo.CurrentCulture);

		// build fully, then swap: a formatting or parse failure above throws before
		// we touch Inlines, so the existing content stays put instead of being blanked
		var built = new List<Inline>(MarkdownParser.Default.ToInlines(text));
		Inlines.Clear();
		foreach (var inline in built) {
			Inlines.Add(inline);
		}
	}
}
