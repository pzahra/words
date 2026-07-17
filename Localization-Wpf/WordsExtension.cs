using System;
using System.Windows;
using System.Windows.Markup;

namespace PatTech.Localization.Wpf; 
public class WordsExtension : MarkupExtension {
	private string value;

	private string _Key;
	[ConstructorArgument("key")]
	public string Key {
		get => _Key;
		set => this.value = Words.Known[_Key = value];
	}

	public WordsExtension() {
		_Key = "?";
		value = "#?#";
	}
	public WordsExtension(string key) => value = Words.Known[_Key = key];

	public override object ProvideValue(IServiceProvider serviceProvider) => value;
}

public static class WordsExtensions {
	public static WordsBuilder LoadResource(this WordsBuilder wb, string packUri) {
		using var stream = Application.GetResourceStream(new(packUri)).Stream;
		wb.Load(stream);
		return wb;
	}
}