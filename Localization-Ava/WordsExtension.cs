using Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using System;
using System.Collections.Generic;

namespace PatTech.Localization.Avalonia;

/// <summary>
///     The <c>{l:Words key}</c> markup extension. Resolves a key against <see cref="Words.Known"/>
///     and hands the localized string to the target property — or, in live mode, a
///     binding that follows the language.
/// </summary>
/// <remarks>
///     Off (<see cref="Words.Live"/> is <see langword="null"/>, the default) the value is
///     resolved once, when <see cref="Key"/> is assigned — it does not re-resolve if
///     <see cref="Words.Known"/> is replaced later. Live, a styled or direct property gets a
///     one-way binding to the key's shared <see cref="LazyWords"/> (<see cref="LazyWords.Of"/>),
///     so <see cref="Words.SwitchLanguage"/> relocalizes it in place; a plain property that
///     can hold no binding still gets the string, resolved once. A key with no Words
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
	/// <returns>The localized string, a <c>#key#</c> placeholder if the key was unknown, or the live binding.</returns>
	public override object ProvideValue(IServiceProvider serviceProvider) {
		if (Words.Live is null
				|| serviceProvider?.GetService(typeof(IProvideValueTarget)) is not IProvideValueTarget { TargetObject: AvaloniaObject target, TargetProperty: AvaloniaProperty property }) {
			return value;
		}
		LazyWords holder = LazyWords.Of(_Key);
		Hold(target, holder);
		var binding = new Binding(nameof(LazyWords.Value)) { Source = holder, Mode = BindingMode.OneWay };
		if (property.PropertyType == typeof(object)) {
			//an object-typed property would take the binding itself for its value: bind
			//it here, and hand back the current text for the loader to set meanwhile
			target.Bind(property, binding);
			return value;
		}
		return binding;
	}

	//Avalonia holds a binding's source weakly, and the shared holder's other owners
	//are weak too, so the first collection would take it and the text would stop
	//following: the target keeps its holders for as long as it lives
	private static readonly AttachedProperty<List<LazyWords>?> HoldersProperty =
		AvaloniaProperty.RegisterAttached<WordsExtension, AvaloniaObject, List<LazyWords>?>("Holders");

	private static void Hold(AvaloniaObject target, LazyWords holder) {
		List<LazyWords>? holders = target.GetValue(HoldersProperty);
		if (holders is null) {
			holders = new List<LazyWords>();
			target.SetValue(HoldersProperty, holders);
		}
		holders.Add(holder);
	}
}


/// <summary>
/// Avalonia-flavored helpers for <see cref="WordsBuilder"/>.
/// </summary>
public static class WordsExtensions {
	/// <summary>
	///     Loads a Words file straight out of the application's embedded assets
	///     (e.g. <c>avares://My-Project/Assets/words.ini</c>).
	/// </summary>
	/// <param name="wb">The builder to load into.</param>
	/// <param name="avaResUri">The <c>avares:</c> URI of the asset to read.</param>
	/// <returns>The same builder, for chaining.</returns>
	public static WordsBuilder LoadResource(this WordsBuilder wb, string avaResUri) {
		using var stream = AssetLoader.Open(new(avaResUri));
		wb.Load(stream);
		return wb;
	}
}
