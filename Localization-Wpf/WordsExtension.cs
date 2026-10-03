using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace PatTech.Localization.Wpf;
/// <summary>
///     The <c>{l:Words key}</c> markup extension. Resolves a key against <see cref="Words.Known"/>
///     and hands the localized string to the target property — or, in live mode, a
///     binding that follows the language.
/// </summary>
/// <remarks>
///     Off (<see cref="Words.Live"/> is <see langword="null"/>, the default) the value is
///     resolved once, when <see cref="Key"/> is assigned — it does not re-resolve if
///     <see cref="Words.Known"/> is replaced later. Live, a dependency property, or a style
///     setter, gets a one-way binding to the key's shared <see cref="LazyWords"/>
///     (<see cref="LazyWords.Of"/>), so <see cref="Words.SwitchLanguage"/> relocalizes it in
///     place; a plain property that can hold no binding — a <c>ConverterParameter</c>, a
///     <c>StringFormat</c> — still gets the string, resolved once. A key with no Words
///     renders as <c>#key#</c>, so missing entries announce themselves instead of hiding.
/// </remarks>
public class WordsExtension : MarkupExtension {
	private string value;

	private string _Key;
	/// <summary>
	///     The key of the Words to provide. Assigning it immediately resolves the value
	///     from <see cref="Words.Known"/>; unknown keys resolve to <c>#key#</c>.
	/// </summary>
	[ConstructorArgument("key")]
	public string Key {
		get => _Key;
		set => this.value = Words.Known[_Key = value];
	}

	/// <summary>
	///     Creates the extension with no key. Until <see cref="Key"/> is set, the provided
	///     value is the placeholder <c>#?#</c>.
	/// </summary>
	public WordsExtension() {
		_Key = "?";
		value = "#?#";
	}
	/// <summary>
	///     Creates the extension and immediately resolves <paramref name="key"/> against
	///     <see cref="Words.Known"/>.
	/// </summary>
	/// <param name="key">The key of the Words to provide.</param>
	public WordsExtension(string key) => value = Words.Known[_Key = key];

	/// <summary>
	///     Returns the localized string resolved from <see cref="Key"/> — or, in live mode
	///     and where the target can hold one, a binding to the key's shared proxy.
	/// </summary>
	/// <param name="serviceProvider">Service provider supplied by the XAML processor; consulted for the target in live mode.</param>
	/// <returns>The localized string, a <c>#key#</c> placeholder if the key was unknown, or what the live binding provides.</returns>
	public override object ProvideValue(IServiceProvider serviceProvider) {
		if (Words.Live is null || serviceProvider?.GetService(typeof(IProvideValueTarget)) is not IProvideValueTarget target) {
			return value;
		}
		//a binding lands on a dependency property, or in a setter that applies one per
		//element; anywhere else — a ConverterParameter, a StringFormat — takes the string
		bool bindable = target.TargetObject is Setter
			|| (target.TargetProperty is DependencyProperty && target.TargetObject is DependencyObject);
		if (!bindable) {
			return value;
		}
		var binding = new Binding(nameof(LazyWords.Value)) { Source = LazyWords.Of(_Key), Mode = BindingMode.OneWay };
		return binding.ProvideValue(serviceProvider);
	}
}

/// <summary>
/// WPF-flavored helpers for <see cref="WordsBuilder"/>.
/// </summary>
public static class WordsExtensions {
	/// <summary>
	///     Loads a Words file straight out of the application's pack resources
	///     (e.g. <c>pack://application:,,,/My-Project;Component/Assets/words.ini</c>).
	/// </summary>
	/// <param name="wb">The builder to load into.</param>
	/// <param name="packUri">The pack URI of the resource to read.</param>
	/// <returns>The same builder, for chaining.</returns>
	public static WordsBuilder LoadResource(this WordsBuilder wb, string packUri) {
		using var stream = Application.GetResourceStream(new(packUri)).Stream;
		wb.Load(stream);
		return wb;
	}
}
