using System.Runtime.InteropServices;
using System.Windows.Markup;

namespace WordsEdit.Utils;

/// <summary>
///     Whether Windows has a spell checker for a language: the same factory WPF's
///     speller asks, since a box whose language it declines is checked by nothing
///     and nobody is told. Answers are cached. A system without the factory, or a
///     tag it cannot place, counts as covered: there is nothing to report.
/// </summary>
public static class SpellCheckers {
	private static readonly Dictionary<string, bool> known = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Lazy<ISpellCheckerFactory?> factory = new(() => {
		try {
			return (ISpellCheckerFactory)new SpellCheckerFactory();
		}
		catch (Exception ex) when (ex is COMException or InvalidCastException) {
			return null;
		}
	});

	/// <summary>Whether a dictionary stands behind <paramref name="languageCode"/>, an IETF tag such as <c>it</c> or <c>pt-BR</c>.</summary>
	public static bool IsInstalled(string languageCode) {
		if (!known.TryGetValue(languageCode, out bool installed)) {
			known[languageCode] = installed = Query(languageCode);
		}
		return installed;
	}

	private static bool Query(string languageCode) {
		string tag;
		try {
			//"it" asks for it-IT, as the speller itself does
			tag = XmlLanguage.GetLanguage(languageCode).GetSpecificCulture().Name;
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) {
			return true;
		}
		if (tag.Length == 0 || factory.Value is not { } checkers) {
			return true;
		}
		try {
			return checkers.IsSupported(tag);
		}
		catch (COMException) {
			return true;
		}
	}

	//the Windows spell checking API (msspellcheckerfactory.h), as far as asking goes
	[ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface ISpellCheckerFactory {
		System.Runtime.InteropServices.ComTypes.IEnumString SupportedLanguages { get; }

		[return: MarshalAs(UnmanagedType.Bool)]
		bool IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
	}

	[ComImport, Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")]
	private class SpellCheckerFactory;
}
