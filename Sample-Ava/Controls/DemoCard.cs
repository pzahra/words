using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using PatTech.Localization.Avalonia;
using System;
using System.Collections.Generic;

namespace Sample_Ava.Controls {
	/// <summary>
	///     One demonstration (SPEC: Cards): a heading, a sentence or two of guidance, the
	///     live result as its content, and chips naming what it uses. <see cref="Key"/>
	///     names the card's words: its value is the heading and <c>.guide</c> beneath it
	///     the guidance, both rendered with markdown. Beneath, a How shows the markup and
	///     the words the card is made of (<see cref="CardHowConverter"/>). The look is App.axaml's.
	/// </summary>
	public class DemoCard : ContentControl {
		public static readonly StyledProperty<string?> KeyProperty =
			AvaloniaProperty.Register<DemoCard, string?>(nameof(Key));

		public static readonly StyledProperty<string?> MoreKeysProperty =
			AvaloniaProperty.Register<DemoCard, string?>(nameof(MoreKeys));

		public static readonly StyledProperty<string?> UsesProperty =
			AvaloniaProperty.Register<DemoCard, string?>(nameof(Uses));

		public static readonly DirectProperty<DemoCard, IReadOnlyList<string>> UsesListProperty =
			AvaloniaProperty.RegisterDirect<DemoCard, IReadOnlyList<string>>(nameof(UsesList), card => card.UsesList);

		private IReadOnlyList<string> usesList = [];
		private TextBlock? heading;
		private TextBlock? guide;

		/// <summary>The card's key: the heading's words, with the guidance at <c>.guide</c>.</summary>
		public string? Key {
			get => GetValue(KeyProperty);
			set => SetValue(KeyProperty, value);
		}

		/// <summary>
		///     Keys the card looks up in code, comma-separated, whose words join its How beside
		///     its own: a <c>[Words]</c> enum's, say. What the words reference joins by itself.
		/// </summary>
		public string? MoreKeys {
			get => GetValue(MoreKeysProperty);
			set => SetValue(MoreKeysProperty, value);
		}

		/// <summary>What the card uses, comma-separated: an extension, an inline, a converter, a scheme.</summary>
		public string? Uses {
			get => GetValue(UsesProperty);
			set => SetValue(UsesProperty, value);
		}

		/// <summary><see cref="Uses"/>, one chip each.</summary>
		public IReadOnlyList<string> UsesList {
			get => usesList;
			private set => SetAndRaise(UsesListProperty, ref usesList, value);
		}

		protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
			base.OnApplyTemplate(e);
			heading = e.NameScope.Find<TextBlock>("PART_Heading");
			guide = e.NameScope.Find<TextBlock>("PART_Guide");
			Fill();
		}

		protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
			base.OnPropertyChanged(change);
			if (change.Property == KeyProperty) {
				Fill();
			}
			else if (change.Property == UsesProperty) {
				UsesList = change.GetNewValue<string?>()?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
			}
		}

		//WordsInline rather than a string: the markdown renders, and a language switch re-renders it
		private void Fill() {
			Show(heading, Key);
			Show(guide, Key is null ? null : Key + ".guide");
		}

		private static void Show(TextBlock? block, string? key) {
			if (block is not null) {
				block.Inlines = key is null ? null : new InlineCollection { new WordsInline { Key = key } };
			}
		}
	}
}
