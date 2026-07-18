using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace PatTech.Localization.Avalonia;

/// <summary>
///     The <c>{l:Words key}</c> markup extension. Resolves a key against <see cref="Words.Known"/>
///     and hands the localized string to the target property.
/// </summary>
/// <remarks>
///     The value is resolved once, when <see cref="Key"/> is assigned — it does not re-resolve
///     if <see cref="Words.Known"/> is replaced later. A key with no Words renders as
///     <c>#key#</c>, so missing entries announce themselves instead of hiding.
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
	///     Returns the localized string resolved from <see cref="Key"/>.
	/// </summary>
	/// <param name="serviceProvider">Service provider supplied by the XAML processor; unused.</param>
	/// <returns>The localized string, or a <c>#key#</c> placeholder if the key was unknown.</returns>
	public override object ProvideValue(IServiceProvider serviceProvider) => value;
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