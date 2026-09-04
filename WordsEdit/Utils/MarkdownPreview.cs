using PatTech.Localization.Wpf;
using System.Windows;
using System.Windows.Controls;

namespace WordsEdit.Utils;

/// <summary>
///     Binds Words markdown into a <see cref="TextBlock"/>: <see cref="TextProperty"/>
///     is rendered to inlines whenever it changes, images resolving through
///     <see cref="ImageFoldersProperty"/> — scheme → folder for the file being
///     previewed, and nothing else.
/// </summary>
/// <remarks>
///     The parser starts schemeless on purpose: the editor is not the host app, so
///     the stock resolvers (staticres/pack/resx/assets) would resolve against
///     Wordsmith's own resources. Only the mapped folders are registered, rebuilt
///     per render; anything unmapped falls back to alt text, and nothing is fetched
///     remotely.
/// </remarks>
public static class MarkdownPreview {
	private static readonly MarkdownParser parser = new();

	public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
		"Text", typeof(string), typeof(MarkdownPreview), new PropertyMetadata(null, Changed));

	public static readonly DependencyProperty ImageFoldersProperty = DependencyProperty.RegisterAttached(
		"ImageFolders", typeof(IReadOnlyDictionary<string, string>), typeof(MarkdownPreview), new PropertyMetadata(null, Changed));

	public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);
	public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

	public static IReadOnlyDictionary<string, string>? GetImageFolders(DependencyObject element)
		=> (IReadOnlyDictionary<string, string>?)element.GetValue(ImageFoldersProperty);
	public static void SetImageFolders(DependencyObject element, IReadOnlyDictionary<string, string>? value)
		=> element.SetValue(ImageFoldersProperty, value);

	private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not TextBlock textBlock) {
			return;
		}
		parser.ImageSchemes.Clear();
		if (GetImageFolders(textBlock) is { } folders) {
			foreach (var (scheme, folder) in folders) {
				parser.ImageSchemes[scheme] = new FolderImageResolver(folder);
			}
		}
		textBlock.Inlines.Clear();
		if (GetText(textBlock) is { Length: > 0 } text) {
			textBlock.Inlines.Add(parser.ToInline(text));
		}
	}
}
