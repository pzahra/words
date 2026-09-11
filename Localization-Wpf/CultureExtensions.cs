using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;

namespace PatTech.Localization.Wpf {
	/// <summary>
	///     WPF culture helpers for the Words runtime.
	/// </summary>
	public static class CultureExtensions {
		private static bool frameworkElementCultureApplied;

		/// <inheritdoc cref="Digest(WordsBuilder, string, out IEnumerable{KeyValuePair{string, string}}, bool)"/>
		public static IWords Digest(this WordsBuilder builder, string languageCode, bool includeFrameworkElements) {
			ArgumentNullException.ThrowIfNull(builder);
			var words = builder.Digest(languageCode);
			if (includeFrameworkElements) ApplyToFrameworkElements();
			return words;
		}

		/// <summary>
		///     The core <see cref="WordsBuilder.Digest(string, out IEnumerable{KeyValuePair{string, string}})"/>
		///     — build, install as <see cref="Words.Known"/>, sync the thread cultures — plus
		///     the one WPF step apps tend to leave out: when
		///     <paramref name="includeFrameworkElements"/> is true, the default
		///     <see cref="FrameworkElement.LanguageProperty"/> is pointed at the formatting
		///     culture just installed, so bindings — <c>StringFormat</c>,
		///     <see cref="WordsConverter"/>, anyone's converter — format in it instead of
		///     WPF's built-in <c>en-US</c>.
		/// </summary>
		/// <remarks>
		///     <see cref="WordsInline"/> and <c>Words.Format</c> use the thread's
		///     <see cref="CultureInfo.CurrentCulture"/> and follow the language either way;
		///     bindings take the target element's <c>Language</c>, which is what the flag
		///     repoints — for controls and for the <see cref="TextElement"/> flow content a
		///     bound <see cref="Run"/> lives in. It points at whichever formatting culture
		///     <c>Digest</c> installed: the language's, or the system's after
		///     <see cref="WordsBuilder.UseSystemNumbers"/>, so English words with system
		///     numbers reach the bindings too. A single element or binding can still say
		///     otherwise with <c>xml:lang</c> or <c>ConverterCulture</c>. The override is
		///     process-global and one-shot — the first call sets it and later calls leave it
		///     — so apply it once at startup, in keeping with the rest of the library's
		///     contract; leave it off to keep WPF's own default.
		/// </remarks>
		/// <param name="builder">The builder with your words loaded.</param>
		/// <param name="languageCode">The language to select, e.g. <c>"en"</c> or <c>"en-GB"</c>.</param>
		/// <param name="languages">Return a list of available languages (see <see cref="WordsBuilder.GetLanguages"/>)</param>
		/// <param name="includeFrameworkElements">Also make WPF bindings default to the installed formatting culture.</param>
		/// <returns>The installed dictionary, the same object now in <see cref="Words.Known"/>.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
		public static IWords Digest(this WordsBuilder builder, string languageCode, out IEnumerable<KeyValuePair<string, string>> languages, bool includeFrameworkElements) {
			ArgumentNullException.ThrowIfNull(builder);
			var words = builder.Digest(languageCode, out languages);
			if (includeFrameworkElements) ApplyToFrameworkElements();
			return words;
		}

		/// <summary>
		///     Points WPF's default <c>Language</c> at the current culture, which the Digest
		///     just set. Once per process: OverrideMetadata cannot be called twice for the
		///     same type.
		/// </summary>
		private static void ApplyToFrameworkElements() {
			if (frameworkElementCultureApplied) return;
			var language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
			// controls, then flow content: a default is not inherited down the tree, and a
			// bound Run is a FrameworkContentElement, which owns its own en-US metadata
			// (AddOwner) and cannot be overridden again — TextElement, the root of every
			// inline and block, can
			FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(language));
			FrameworkElement.LanguageProperty.OverrideMetadata(typeof(TextElement), new FrameworkPropertyMetadata(language));
			frameworkElementCultureApplied = true;
		}
	}
}
