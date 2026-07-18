using System;
using System.Collections.Generic;
using System.Text;

namespace PatTech.Localization {
	/// <summary>
	/// Gives you Words in the terminal: renders Words markdown as a string decorated
	/// with ANSI escape codes. Bold and italic use SGR styling, links become real
	/// clickable hyperlinks (OSC 8) underlined and blue in the traditional manner,
	/// sub/superscript translate to their Unicode forms where such forms exist, and
	/// images degrade gracefully to their alt text.
	/// </summary>
	/// <remarks>
	/// The output assumes a VT-capable terminal (Windows Terminal, and practically
	/// everything that is not <c>conhost.exe</c> from a bygone era). Pass
	/// <c>useAnsi: false</c> — or let <c>Console.WriteWords</c> decide from
	/// <see cref="Console.IsOutputRedirected"/> — to get plain text with links spelled
	/// out as <c>text (url)</c>, safe for pipes and log files.
	/// </remarks>
	/// <param name="useAnsi">Whether to emit ANSI escape codes; <see langword="false"/> produces undecorated text.</param>
	/// <param name="logger">Receives parse errors; <see langword="null"/> discards them.</param>
	public class ConsoleMarkdownParser(bool useAnsi = true, ITakeException? logger = null)
		: MarkdownParser<string>(logger) {
		private const string Esc = "\x1b";

		/// <summary>Plain text passes through untouched.</summary>
		protected override string Run(string text) => text;
		/// <summary>Adjacent inlines simply concatenate.</summary>
		protected override string Span(IEnumerable<string> inlines) => string.Concat(inlines);

		/// <summary>
		/// Renders a link. With ANSI enabled this is an OSC 8 hyperlink — genuinely
		/// clickable in modern terminals — underlined and blue; without ANSI it renders
		/// as <c>text (url)</c>, or just the url when the text adds nothing.
		/// The tooltip has nowhere to live in a terminal and is dropped.
		/// </summary>
		protected override string Hyperlink(string content, Uri target, string? tooltip) {
			if (!useAnsi) {
				return content == target.OriginalString
					? content
					: $"{content} ({target.OriginalString})";
			}
			return $"{Esc}]8;;{target.OriginalString}{Esc}\\{Esc}[4;34m{content}{Esc}[24;39m{Esc}]8;;{Esc}\\";
		}

		/// <summary>
		/// A terminal cannot show the picture, so the image renders as its alt text
		/// (or its address, if somehow there is no alt text). The tooltip is dropped.
		/// </summary>
		protected override string Image(Uri source, string? altText, string? tooltip)
			=> altText ?? source.OriginalString;

		/// <summary>Wraps the content in SGR bold (<c>CSI 1 m</c> … <c>CSI 22 m</c>).</summary>
		protected override void Embolden(ref string content) {
			if (useAnsi) content = $"{Esc}[1m{content}{Esc}[22m";
		}
		/// <summary>Wraps the content in SGR italic (<c>CSI 3 m</c> … <c>CSI 23 m</c>).</summary>
		protected override void Italicize(ref string content) {
			if (useAnsi) content = $"{Esc}[3m{content}{Esc}[23m";
		}
		/// <summary>
		/// Translates the content to Unicode subscript characters, best effort:
		/// characters with no subscript form stay at full size on the baseline.
		/// </summary>
		protected override void Subscript(ref string content) => content = Translate(content, SubscriptMap);
		/// <summary>
		/// Translates the content to Unicode superscript characters, best effort:
		/// characters with no superscript form (<c>q</c>, notoriously) stay at full
		/// size on the baseline.
		/// </summary>
		protected override void Superscript(ref string content) => content = Translate(content, SuperscriptMap);

		private static string Translate(string text, Dictionary<char, char> map) {
			var sb = new StringBuilder(text.Length);
			foreach (char c in text) {
				sb.Append(map.TryGetValue(c, out char mapped) ? mapped : c);
			}
			return sb.ToString();
		}

		private static readonly Dictionary<char, char> SuperscriptMap = new() {
			['0'] = '⁰', ['1'] = '¹', ['2'] = '²', ['3'] = '³', ['4'] = '⁴',
			['5'] = '⁵', ['6'] = '⁶', ['7'] = '⁷', ['8'] = '⁸', ['9'] = '⁹',
			['+'] = '⁺', ['-'] = '⁻', ['='] = '⁼', ['('] = '⁽', [')'] = '⁾',
			['a'] = 'ᵃ', ['b'] = 'ᵇ', ['c'] = 'ᶜ', ['d'] = 'ᵈ', ['e'] = 'ᵉ',
			['f'] = 'ᶠ', ['g'] = 'ᵍ', ['h'] = 'ʰ', ['i'] = 'ⁱ', ['j'] = 'ʲ',
			['k'] = 'ᵏ', ['l'] = 'ˡ', ['m'] = 'ᵐ', ['n'] = 'ⁿ', ['o'] = 'ᵒ',
			['p'] = 'ᵖ', ['r'] = 'ʳ', ['s'] = 'ˢ', ['t'] = 'ᵗ', ['u'] = 'ᵘ',
			['v'] = 'ᵛ', ['w'] = 'ʷ', ['x'] = 'ˣ', ['y'] = 'ʸ', ['z'] = 'ᶻ',
		};
		private static readonly Dictionary<char, char> SubscriptMap = new() {
			['0'] = '₀', ['1'] = '₁', ['2'] = '₂', ['3'] = '₃', ['4'] = '₄',
			['5'] = '₅', ['6'] = '₆', ['7'] = '₇', ['8'] = '₈', ['9'] = '₉',
			['+'] = '₊', ['-'] = '₋', ['='] = '₌', ['('] = '₍', [')'] = '₎',
			['a'] = 'ₐ', ['e'] = 'ₑ', ['h'] = 'ₕ', ['i'] = 'ᵢ', ['j'] = 'ⱼ',
			['k'] = 'ₖ', ['l'] = 'ₗ', ['m'] = 'ₘ', ['n'] = 'ₙ', ['o'] = 'ₒ',
			['p'] = 'ₚ', ['r'] = 'ᵣ', ['s'] = 'ₛ', ['t'] = 'ₜ', ['u'] = 'ᵤ',
			['v'] = 'ᵥ', ['x'] = 'ₓ',
		};
	}

#if NET10_0_OR_GREATER
	/// <summary>
	/// Puts Words in the <see cref="Console"/>: <c>Console.WriteWords("main.title")</c>
	/// looks the key up in <see cref="Words.Known"/>, renders its markdown via
	/// <see cref="ConsoleMarkdownParser"/>, and writes the result. ANSI decoration is
	/// used when output goes to a terminal and skipped when it is redirected.
	/// (.NET 10 and later only — earlier targets can use
	/// <see cref="ConsoleMarkdownParser"/> directly.)
	/// </summary>
	public static class WordsConsoleExtensions {
		private static readonly ConsoleMarkdownParser AnsiParser = new(useAnsi: true, ITakeException.Global);
		private static readonly ConsoleMarkdownParser PlainParser = new(useAnsi: false, ITakeException.Global);

		extension(Console) {
			/// <summary>
			/// Writes the rendered value of <paramref name="key"/>, markdown and all.
			/// A missing key writes <c>#key#</c>, exactly as
			/// <see cref="Words.RenderKey(IWordsProvider, string, object[])"/> renders it.
			/// </summary>
			/// <param name="key">The key to look up in <see cref="Words.Known"/>.</param>
			/// <param name="args">Optional arguments applied to the value's <c>{0}</c>-style placeholders.</param>
			public static void WriteWords(string key, params object?[] args)
				=> Console.Out.Write(RenderForConsole(key, args));
			/// <summary>
			/// <see cref="WriteWords(string, object?[])"/>, followed by the line terminator.
			/// </summary>
			/// <param name="key">The key to look up in <see cref="Words.Known"/>.</param>
			/// <param name="args">Optional arguments applied to the value's <c>{0}</c>-style placeholders.</param>
			public static void WriteWordsLine(string key, params object?[] args)
				=> Console.Out.WriteLine(RenderForConsole(key, args));
		}

		private static string RenderForConsole(string key, object?[] args) {
			var parser = Console.IsOutputRedirected ? PlainParser : AnsiParser;
			return parser.ToInline(Words.Known.RenderKey(key, args!));
		}
	}
#endif
}
