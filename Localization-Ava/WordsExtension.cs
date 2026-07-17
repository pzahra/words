using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace PatTech.Localization.Ava;

/// <summary>
/// Provides a markup extension for retrieving known Words based on a specified key.
/// </summary>
/// <remarks>Use this extension in XAML to bind properties to Words. The extension supports specifying the key either
/// via the Key property or constructor. If the key does not correspond to known Words, the returned value may be null.
/// </remarks>
public class WordsExtension : MarkupExtension {
	private string value;

	private string _Key;
	/// <summary>
	/// Gets or sets the key used to identify the Words.
	/// </summary>
	/// <remarks>The key must correspond to known Words. Setting this property
	/// updates the associated value based on the provided key.</remarks>
	[ConstructorArgument("key")]
	public string Key {
		get => _Key;
		set => this.value = Words.Known[_Key = value];
	}

	/// <summary>
	/// Initializes a new instance of the WordsExtension class.
	/// </summary>
	public WordsExtension() {
		_Key = "?";
		value = "#?#";
	}
	/// <summary>
	/// Initializes a new instance of the WordsExtension class using the specified key.
	/// </summary>
	/// <remarks>If the specified key does not exist in the known words collection, the value will be set to null.
	/// Ensure the key is valid to avoid unexpected results.</remarks>
	/// <param name="key">The key used to retrieve the associated value from the known words collection. Cannot be null or empty.</param>
	public WordsExtension(string key) => value = Words.Known[_Key = key];

	/// <summary>
	/// Returns the value provided by this markup extension for use in XAML.
	/// </summary>
	/// <remarks>This method is called by the XAML infrastructure when evaluating the markup extension. The returned
	/// value is assigned to the property where the extension is applied.</remarks>
	/// <param name="serviceProvider">An object that can provide services for the markup extension. Typically used to access contextual information
	/// during XAML processing.</param>
	/// <returns>The object value to set on the target property in XAML.</returns>
	public override object ProvideValue(IServiceProvider serviceProvider) => value;
}


public static class WordsExtensions {
	public static WordsBuilder LoadResource(this WordsBuilder wb, string avaResUri) {
		using var stream = AssetLoader.Open(new(avaResUri));
		wb.Load(stream);
		return wb;
	}

}