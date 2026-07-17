using System;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;

namespace PatTech.Localization.Wpf {
	[ContentProperty(nameof(Params))]
	public class WordsInline : Span {
		private static readonly MarkdownParser markdown = new();

		public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
			nameof(Key),
			typeof(string),
			typeof(WordsInline),
			new PropertyMetadata(propertyChangedCallback: KeyChanged));

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

			Inlines.AddRange(markdown.ToInlines(text));
		}

		public string Key {
			get => (string)GetValue(KeyProperty);
			set => SetValue(KeyProperty, value);
		}

		public object? Params {
			get => GetValue(ParamsProperty);
			set => SetValue(ParamsProperty, value);
		}
	}
}
