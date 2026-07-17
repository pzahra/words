using PatTech.Localization;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;

namespace PatTech.Markdown.Wpf {
	[ContentProperty(nameof(Params))]
	public class MarkdownSpan : Span {
		public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
			nameof(Text),
			typeof(string),
			typeof(MarkdownSpan),
			new PropertyMetadata(propertyChangedCallback: TextChanged));

		public static readonly DependencyProperty ParamsProperty = DependencyProperty.Register(
			nameof(Params),
			typeof(object),
			typeof(MarkdownSpan),
			new PropertyMetadata(propertyChangedCallback: ParamsChanged));

		private static void TextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
			if (d is MarkdownSpan w) {
				w.UpdateChild((string)e.NewValue, w.Params);
			}
		}
		private static void ParamsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
			if (d is MarkdownSpan w) {
				w.UpdateChild(w.Text, e.NewValue);
			}
		}
		private void UpdateChild(string? text, object? @params) {
			Inlines.Clear();

			if (string.IsNullOrEmpty(text)) {
				return;
			}

			switch (@params) {
				case null:
					break;
				case object[] arr:
					text = string.Format(CultureInfo.CurrentUICulture, text, arr);
					break;
				case Array arr: {
					var objs = new object[arr.Length];
					for (int i = 0; i < arr.Length; ++i) {
						objs[i] = arr.GetValue(i)!;
					}
					text = string.Format(CultureInfo.CurrentUICulture, text, objs);
					break;
				}
				default:
					text = Words.FormatByName(CultureInfo.CurrentCulture, text, @params);
					break;
			}

			Inlines.AddRange(MarkdownParser.ToInlines(text));
		}

		public string Text {
			get => (string)GetValue(TextProperty);
			set => SetValue(TextProperty, value);
		}

		public object? Params {
			get => GetValue(ParamsProperty);
			set => SetValue(ParamsProperty, value);
		}
	}

}
