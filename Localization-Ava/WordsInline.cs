using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Metadata;
using System.Globalization;

namespace PatTech.Localization.Ava;

public class WordsInline : Span {
	private static readonly MarkdownParser markdown = new();

	public static readonly StyledProperty<string?> KeyProperty =
		AvaloniaProperty.Register<WordsInline, string?>(nameof(Key));

	public static readonly StyledProperty<object?> ParamsProperty =
		AvaloniaProperty.Register<WordsInline, object?>(nameof(Params));

	public string? Key {
		get => GetValue(KeyProperty);
		set => SetValue(KeyProperty, value);
	}

	[Content]
	public object? Params {
		get => GetValue(ParamsProperty);
		set => SetValue(ParamsProperty, value);
	}

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
		Inlines.Clear();

		if (string.IsNullOrEmpty(key))
			return;

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
				for (int i = 0; i < arr.Length; ++i)
					objs[i] = arr.GetValue(i)!;

				text = string.Format(CultureInfo.CurrentUICulture, Words.Known[key], objs);
				break;
			}

			default: {
				text = Words.FormatByName(
					CultureInfo.CurrentCulture,
					Words.Known[key],
					@params);
				break;
			}
		}

		foreach (var inline in markdown.ToInlines(text)) {
			Inlines.Add(inline);
		}
	}
}
