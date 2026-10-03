using PatTech.Localization.Wpf;
using System.Windows;
using System.Windows.Controls;

namespace Sample_Wpf.Controls {
	/// <summary>
	///     One demonstration (SPEC: Cards): a heading, a sentence or two of guidance, the
	///     live result as its content, and chips naming what it uses. <see cref="Key"/>
	///     names the card's words: its value is the heading and <c>.guide</c> beneath it
	///     the guidance, both rendered with markdown. The look is App.xaml's.
	/// </summary>
	public class DemoCard : ContentControl {
		public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
			nameof(Key), typeof(string), typeof(DemoCard), new PropertyMetadata(null, (d, _) => ((DemoCard)d).Fill()));

		public static readonly DependencyProperty UsesProperty = DependencyProperty.Register(
			nameof(Uses), typeof(string), typeof(DemoCard), new PropertyMetadata(null, (d, e) => ((DemoCard)d).SetValue(UsesListKey, Split((string?)e.NewValue))));

		private static readonly DependencyPropertyKey UsesListKey = DependencyProperty.RegisterReadOnly(
			nameof(UsesList), typeof(IReadOnlyList<string>), typeof(DemoCard), new PropertyMetadata(Array.Empty<string>()));
		public static readonly DependencyProperty UsesListProperty = UsesListKey.DependencyProperty;

		private TextBlock? heading;
		private TextBlock? guide;

		/// <summary>The card's key: the heading's words, with the guidance at <c>.guide</c>.</summary>
		public string? Key {
			get => (string?)GetValue(KeyProperty);
			set => SetValue(KeyProperty, value);
		}

		/// <summary>What the card uses, comma-separated: an extension, an inline, a converter, a scheme.</summary>
		public string? Uses {
			get => (string?)GetValue(UsesProperty);
			set => SetValue(UsesProperty, value);
		}

		/// <summary><see cref="Uses"/>, one chip each.</summary>
		public IReadOnlyList<string> UsesList => (IReadOnlyList<string>)GetValue(UsesListProperty);

		public override void OnApplyTemplate() {
			base.OnApplyTemplate();
			heading = GetTemplateChild("PART_Heading") as TextBlock;
			guide = GetTemplateChild("PART_Guide") as TextBlock;
			Fill();
		}

		//WordsInline rather than a string: the markdown renders, and a language switch re-renders it
		private void Fill() {
			Show(heading, Key);
			Show(guide, Key is null ? null : Key + ".guide");
		}

		private static void Show(TextBlock? block, string? key) {
			if (block is null) {
				return;
			}
			block.Inlines.Clear();
			if (key is not null) {
				block.Inlines.Add(new WordsInline { Key = key });
			}
		}

		private static string[] Split(string? uses)
			=> uses?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
	}
}
