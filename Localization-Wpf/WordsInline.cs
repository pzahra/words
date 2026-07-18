using System;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;

namespace PatTech.Localization.Wpf {
	/// <summary>
	///     An inline that renders the Words for <see cref="Key"/> — markdown and all — inside a
	///     <see cref="System.Windows.Controls.TextBlock"/> or other flow content.
	/// </summary>
	/// <remarks>
	///     The resolved text may contain format placeholders, filled from <see cref="Params"/>:
	///     an array supplies positional <c>{0}</c>-style arguments, while any other single object
	///     supplies <c>{Name}</c>-style placeholders looked up by field or property name (see
	///     <see cref="Words.FormatByName(string, object?, object?[])"/>). The rendered inlines are
	///     rebuilt whenever <see cref="Key"/> or <see cref="Params"/> changes.
	/// </remarks>
	[ContentProperty(nameof(Params))]
	public class WordsInline : Span {
		/// <summary>Identifies the <see cref="Key"/> dependency property.</summary>
		public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
			nameof(Key),
			typeof(string),
			typeof(WordsInline),
			new PropertyMetadata(propertyChangedCallback: KeyChanged));

		/// <summary>Identifies the <see cref="Params"/> dependency property.</summary>
		public static readonly DependencyProperty ParamsProperty = DependencyProperty.Register(
			nameof(Params),
			typeof(object),
			typeof(WordsInline),
			new PropertyMetadata(propertyChangedCallback: ParamsChanged));

		private static void KeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
			if (d is WordsInline w) {
				w.UpdateChild((string)e.NewValue, w.Params);
			}
		}
		private static void ParamsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
			if (d is WordsInline w) {
				w.UpdateChild(w.Key, e.NewValue);
			}
		}
		private void UpdateChild(string? key, object? @params) {
			Inlines.Clear();

			if (string.IsNullOrEmpty(key)) {
				return;
			}

			string text;
			switch (@params) {
				case null:
					text = Words.Known[key];
					break;
				case object[] arr:
					text = string.Format(CultureInfo.CurrentUICulture, Words.Known[key], arr);
					break;
				case Array arr: {
					var objs = new object[arr.Length];
					for (int i = 0; i < arr.Length; ++i) {
						objs[i] = arr.GetValue(i)!;
					}
					text = string.Format(CultureInfo.CurrentUICulture, Words.Known[key], objs);
					break;
				}
				default:
					text = Words.FormatByName(CultureInfo.CurrentCulture, Words.Known[key], @params);
					break;
			}

			Inlines.AddRange(MarkdownParser.Default.ToInlines(text));
		}

		/// <summary>
		///     The key of the Words to render. A null or empty key renders nothing;
		///     an unknown key renders as <c>#key#</c>.
		/// </summary>
		public string Key {
			get => (string)GetValue(KeyProperty);
			set => SetValue(KeyProperty, value);
		}

		/// <summary>
		///     Optional arguments for the resolved text's format placeholders. An array fills
		///     positional <c>{0}</c> placeholders; any other object fills <c>{Name}</c>
		///     placeholders from its public fields and properties. This is the content property,
		///     so a single argument can be written as the element's content in XAML.
		/// </summary>
		public object? Params {
			get => GetValue(ParamsProperty);
			set => SetValue(ParamsProperty, value);
		}
	}
}
