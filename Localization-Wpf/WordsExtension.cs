using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace PatTech.Localization.Wpf;
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
///     re-resolve if <see cref="Words.Known"/> is replaced later. Live, a dependency property,
///     or a style setter, gets a one-way binding to the key's shared <see cref="LazyWords"/>
///     (<see cref="LazyWords.Of"/>), so <see cref="Words.SwitchLanguage"/> relocalizes it in
///     place; a plain property that can hold no binding — a <c>ConverterParameter</c>, a
///     <c>StringFormat</c> — still gets the string, resolved once. A key with no Words
///     renders as <c>#key#</c>, so missing entries announce themselves instead of hiding.
///     </para>
///     <para>
///     <c>{l:Words {Binding KeyName}}</c>: a binding with no converter. The bound value is the
///     key, looked up whenever it changes and, live, on every switch.
///     <c>{l:Words {Binding Status, Converter={StaticResource WordsFormat}, ConverterParameter=op.status}}</c>:
///     a binding with a converter of its own, which says how to localize — a template fill,
///     an enum's description. Off, it is handed on untouched; live, its converter runs again
///     on every switch. Either way the result is a binding, so its target must be able to
///     hold one. A <c>MultiBinding</c> or <c>PriorityBinding</c> is handed on untouched.
///     </para>
/// </remarks>
public class WordsExtension : MarkupExtension {
	private string value;
	private readonly BindingBase? wrapped;
	private BindingBase? provided;

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
	///     Creates the extension over a key, resolved against <see cref="Words.Known"/>
	///     immediately, or over a binding, localized when the binding is applied.
	/// </summary>
	/// <param name="key">The key of the Words to provide, or a <see cref="BindingBase"/> whose value to localize.</param>
	/// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
	public WordsExtension(object key) {
		ArgumentNullException.ThrowIfNull(key);
		if (key is BindingBase binding) {
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
	///     binding, what the wrapped binding provides.
	/// </summary>
	/// <param name="serviceProvider">Service provider supplied by the XAML processor; consulted for the target.</param>
	/// <returns>The localized string, a <c>#key#</c> placeholder if the key was unknown, or what a binding provides.</returns>
	public override object ProvideValue(IServiceProvider serviceProvider) {
		if (wrapped is not null) {
			//built once: in a template the extension may be asked again for each instance,
			//and the binding it was given can change only before its first use
			provided ??= Wrap(wrapped, live: Words.Live is not null);
			return provided.ProvideValue(serviceProvider);
		}
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

	//the binding as it localizes: off, a bound key gains a lookup and a converted binding
	//is itself; live, the converter (or the lookup) moves up to a multi-binding with the
	//tickle beside the binding, since a multi-binding re-runs only its own converter
	private static BindingBase Wrap(BindingBase wrapped, bool live) {
		if (wrapped is not Binding binding) {
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
		var converter = new BoundWordsConverter(binding.Converter, binding.ConverterParameter, binding.ConverterCulture);
		var multi = new MultiBinding { Converter = converter, Mode = BindingMode.OneWay, StringFormat = binding.StringFormat };
		binding.Converter = null;
		binding.ConverterParameter = null;
		binding.ConverterCulture = null;
		binding.StringFormat = null;
		multi.Bindings.Add(binding);
		multi.Bindings.Add(new Binding(nameof(WordsTickle.Pulse)) { Source = WordsTickle.Watch(), Mode = BindingMode.OneWay });
		return multi;
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
