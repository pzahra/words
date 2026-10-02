using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;

namespace WordsEdit.Utils;

/// <summary>
///     Spell checking that stays out of the way of browsing. WPF's speller scans
///     at idle priority but at seconds per kilobyte of markdown, and a text
///     checked against the wrong dictionary flags every word. So a box with
///     <see cref="WhenEditingProperty"/> starts checking when it takes the
///     keyboard focus and stops when its text is replaced from the document
///     while the focus is elsewhere — a node being selected — and
///     <see cref="LanguageProperty"/> hands it the dictionary of the language
///     it edits.
/// </summary>
public static class Spelling {
	public static readonly DependencyProperty WhenEditingProperty = DependencyProperty.RegisterAttached(
		"WhenEditing", typeof(bool), typeof(Spelling), new PropertyMetadata(false, WhenEditingChanged));

	/// <summary>The IETF tag of the text's language; blank or malformed leaves the box's inherited language.</summary>
	public static readonly DependencyProperty LanguageProperty = DependencyProperty.RegisterAttached(
		"Language", typeof(string), typeof(Spelling), new PropertyMetadata(null, LanguageChanged));

	public static bool GetWhenEditing(DependencyObject element) => (bool)element.GetValue(WhenEditingProperty);
	public static void SetWhenEditing(DependencyObject element, bool value) => element.SetValue(WhenEditingProperty, value);

	public static string? GetLanguage(DependencyObject element) => (string?)element.GetValue(LanguageProperty);
	public static void SetLanguage(DependencyObject element, string? value) => element.SetValue(LanguageProperty, value);

	private static void WhenEditingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not TextBoxBase box) {
			return;
		}
		if ((bool)e.NewValue) {
			box.GotKeyboardFocus += Focused;
			box.TextChanged += TextChanged;
			SpellCheck.SetIsEnabled(box, box.IsKeyboardFocusWithin);
		}
		else {
			box.GotKeyboardFocus -= Focused;
			box.TextChanged -= TextChanged;
		}
	}

	private static void Focused(object sender, KeyboardFocusChangedEventArgs e) => SpellCheck.SetIsEnabled((TextBoxBase)sender, true);

	//typing keeps the speller; a value arriving with the focus elsewhere does not wake it
	private static void TextChanged(object sender, TextChangedEventArgs e) {
		var box = (TextBoxBase)sender;
		if (!box.IsKeyboardFocusWithin) {
			SpellCheck.SetIsEnabled(box, false);
		}
	}

	private static void LanguageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not FrameworkElement element) {
			return;
		}
		try {
			if (e.NewValue is string { Length: > 0 } code) {
				element.Language = XmlLanguage.GetLanguage(code);
				return;
			}
		}
		catch (ArgumentException) {
			//a placeholder code: no dictionary to name
		}
		element.ClearValue(FrameworkElement.LanguageProperty);
	}
}
