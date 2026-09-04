using PatTech.Localization.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WordsEdit.Utils;

/// <summary>
///     Binds Words markdown into a <see cref="TextBlock"/>: <see cref="TextProperty"/>
///     is rendered to inlines whenever it changes, images resolving through the
///     project's rules in <see cref="SettingsProperty"/> — one resolver per
///     <c>[images]</c> scheme, the rule deciding the file — and nothing else.
/// </summary>
/// <remarks>
///     The parser starts schemeless on purpose: the editor is not the host app, so
///     the stock resolvers (staticres/pack/resx/assets) would resolve against
///     Wordsmith's own resources. Only the project's schemes are registered,
///     rebuilt per render; anything else falls back to alt text, every path is
///     clamped to its rule's folder by <see cref="ProjectSettings.TryResolveImage"/>,
///     and nothing is fetched remotely.
/// </remarks>
public static class MarkdownPreview {
	private static readonly MarkdownParser parser = new();

	public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
		"Text", typeof(string), typeof(MarkdownPreview), new PropertyMetadata(null, Changed));

	public static readonly DependencyProperty SettingsProperty = DependencyProperty.RegisterAttached(
		"Settings", typeof(ProjectSettings), typeof(MarkdownPreview), new PropertyMetadata(null, Changed));

	public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);
	public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);

	public static ProjectSettings? GetSettings(DependencyObject element) => (ProjectSettings?)element.GetValue(SettingsProperty);
	public static void SetSettings(DependencyObject element, ProjectSettings? value) => element.SetValue(SettingsProperty, value);

	private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not TextBlock textBlock) {
			return;
		}
		parser.ImageSchemes.Clear();
		if (GetSettings(textBlock) is { } settings) {
			var resolver = new SettingsImageResolver(settings);
			foreach (ImageRule rule in settings.Images) {
				parser.ImageSchemes[rule.Scheme] = resolver;
			}
		}
		textBlock.Inlines.Clear();
		if (GetText(textBlock) is { Length: > 0 } text) {
			textBlock.Inlines.Add(parser.ToInline(text));
		}
	}

	/// <summary>The project's image rules as the renderer sees them: the file the settings resolve, shown as it is.</summary>
	private sealed class SettingsImageResolver(ProjectSettings settings) : IImageSchemeResolver {
		public FrameworkElement? Resolve(Uri source, ImageOptions options) {
			if (!settings.TryResolveImage(source, out string filePath)) {
				return null;
			}
			var bitmap = new BitmapImage();
			bitmap.BeginInit();
			bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
			bitmap.CacheOption = BitmapCacheOption.OnLoad;
			bitmap.EndInit();
			return new Image { Source = bitmap, Stretch = Stretch.Uniform };
		}
	}
}
