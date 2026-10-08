using Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace PatTech.Localization.Avalonia;

/// <summary>
///     The <c>{l:Words}</c> markup extension. Given a key, resolves it against
///     <see cref="Words.Known"/> and hands the localized string to the target property — or,
///     in live mode, a binding that follows the language. Given a binding, localizes what
///     the binding produces.
/// </summary>
/// <remarks>
///     <para>
///     <c>{l:Words some.key}</c>: off (<see cref="Words.Live"/> is <see langword="null"/>, the
///     default) the value is resolved once, when the key is assigned — it does not
///     re-resolve if <see cref="Words.Known"/> is replaced later. Live, a styled or direct
///     property gets a one-way binding to the key's shared <see cref="LazyWords"/>
///     (<see cref="LazyWords.Of"/>), so <see cref="Words.SwitchLanguage"/> relocalizes it in
///     place; a plain property that can hold no binding still gets the string, resolved
///     once. A key with no Words renders as <c>#key#</c>, so missing entries announce
///     themselves instead of hiding.
///     </para>
///     <para>
///     <c>{l:Words {Binding KeyName}}</c>: a binding with no converter. The bound value is the
///     key, looked up whenever it changes and, live, on every switch.
///     <c>{l:Words {Binding Status, Converter={StaticResource WordsFormat}, ConverterParameter=op.status}}</c>:
///     a binding with a converter of its own, which says how to localize — a template fill,
///     an enum's description. Off, it is handed on untouched; live, its converter runs again
///     on every switch. Compiled bindings and reflection bindings alike; a
///     <c>MultiBinding</c> is handed on untouched.
///     </para>
/// </remarks>
public class WordsExtension : MarkupExtension {
	private string value;
	private readonly IBinding? wrapped;
	private IBinding? provided;

	private string _Key;
	/// <summary>
	///     The key of the Words to provide. Assigning it immediately resolves the value
	///     from <see cref="Words.Known"/>; unknown keys resolve to <c>#key#</c>.
	/// </summary>
	[WordsKey]
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
	///     Creates the extension over a key, resolved against <see cref="Words.Known"/>
	///     immediately, or over a binding, localized when the binding is applied.
	/// </summary>
	/// <param name="key">The key of the Words to provide, or an <see cref="IBinding"/> whose value to localize.</param>
	/// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
	public WordsExtension([WordsKey] object key) {
		ArgumentNullException.ThrowIfNull(key);
		if (key is IBinding binding) {
			wrapped = binding;
			_Key = "?";
			value = "#?#";
		}
		else {
			value = Words.Known[_Key = key.ToString() ?? ""];
		}
	}

	/// <summary>
	///     Returns the localized string resolved from <see cref="Key"/> — or, in live mode
	///     and where the target can hold one, a binding to the key's shared proxy — or, over a
	///     binding, the binding that localizes it.
	/// </summary>
	/// <param name="serviceProvider">Service provider supplied by the XAML processor; consulted for the target.</param>
	/// <returns>The localized string, a <c>#key#</c> placeholder if the key was unknown, or a binding.</returns>
	public override object ProvideValue(IServiceProvider serviceProvider) {
		var target = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
		if (wrapped is not null) {
			//built once, since the binding it was given can change only before its first use
			provided ??= Wrap(wrapped, live: Words.Live is not null);
			return Hand(target, provided);
		}
		if (Words.Live is null || target is not { TargetObject: AvaloniaObject owner, TargetProperty: AvaloniaProperty }) {
			return value;
		}
		LazyWords holder = LazyWords.Of(_Key);
		Hold(owner, holder);
		return Hand(target, new Binding(nameof(LazyWords.Value)) { Source = holder, Mode = BindingMode.OneWay });
	}

	//an object-typed property would take a binding itself for its value: bind it here,
	//and hand back what it shows now for the loader to set meanwhile
	private static object Hand(IProvideValueTarget? target, IBinding binding) {
		if (target is { TargetObject: AvaloniaObject owner, TargetProperty: AvaloniaProperty property } && property.PropertyType == typeof(object)) {
			owner.Bind(property, binding);
			return owner.GetValue(property) ?? AvaloniaProperty.UnsetValue;
		}
		return binding;
	}

	//BindingBase.ConverterCulture arrived after Avalonia 11.0, which the package still takes
	private static readonly PropertyInfo? ConverterCulture = typeof(BindingBase).GetProperty("ConverterCulture");

	//the binding as it localizes: off, a bound key gains a lookup and a converted binding
	//is itself; live, the converter (or the lookup) moves up to a multi-binding with the
	//trigger beside the binding, since a multi-binding re-runs only its own converter
	private static IBinding Wrap(IBinding wrapped, bool live) {
		if (wrapped is not BindingBase binding) {
			return wrapped;
		}
		if (!live) {
			if (binding.Converter is null) {
				binding.Converter = new BoundWordsConverter();
				if (binding.Mode == BindingMode.Default) {
					binding.Mode = BindingMode.OneWay;
				}
			}
			return binding;
		}
		var converter = new BoundWordsConverter(binding.Converter, binding.ConverterParameter, ConverterCulture?.GetValue(binding) as CultureInfo);
		var multi = new MultiBinding { Converter = converter, Mode = BindingMode.OneWay, StringFormat = binding.StringFormat };
		binding.Converter = null;
		binding.ConverterParameter = null;
		ConverterCulture?.SetValue(binding, null);
		binding.StringFormat = null;
		multi.Bindings.Add(binding);
		multi.Bindings.Add(new Binding(nameof(TriggerWords.Pulse)) { Source = TriggerWords.Watch(), Mode = BindingMode.OneWay });
		return multi;
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
