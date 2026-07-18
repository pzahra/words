using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PatTech.Utils;

namespace PatTech.Localization;

/// <summary>
/// Parses a subset of inline markdown into framework-specific inline objects,
/// built by the abstract factory methods a subclass supplies. Supported syntax:
/// <c>*italic*</c>, <c>**bold**</c>, <c>***both***</c>, <c>^superscript^</c>,
/// <c>~subscript~</c>, links as <c>[text](url "title")</c> or <c>&lt;url&gt;</c>,
/// images as <c>![alt](url "title")</c>, HTML entities (<c>&amp;amp;</c>,
/// <c>&amp;#65;</c>, <c>&amp;#x41;</c>) and <c>:emoji:</c> shortcodes. Block-level
/// markdown (headings, lists, paragraphs) is out of scope.
/// </summary>
/// <typeparam name="TInline">The framework's inline type, e.g. a WPF <c>Inline</c>.</typeparam>
public abstract class MarkdownParser<TInline> : IMarkdownParser<TInline> {
	private static readonly Regex MarkdownTextFormatPattern = new(
		pattern: @"
# italic, bold, or both using `*`, `**`, or `***` respectively
 (?<basic>\*{1,3})(?=[\S-[*]])(?<text>.+?)(?<=[\S-[*]])\k<basic>
|(?<basic>\^)(?=[\S-[\^]])(?<text>.+?)(?<=[\S-[\^]])\k<basic>
|(?<basic>~)(?=[\S-[~]])(?<text>.+?)(?<=[\S-[~]])\k<basic>
# full link `[text](url)` or `[label](url ""title"")`, or image with leading `!`
|(?<image>!)?\[(?<text>[^]]+)\]\(\s*(?<url>[^\s)]+)(\s+""(?<title>[^""]*)"")?\s*\)
# simple link `<url>`
|<\s*(?<text>(?<url>[^\s>]+))\s*>",
		options: RegexOptions.Compiled
			| RegexOptions.IgnorePatternWhitespace
			| RegexOptions.ExplicitCapture);
	private static readonly Regex EntitiesPattern = new(
		pattern: @"&((?<entity>[a-zA-Z]+)|\#(?<dec>[0-9]+)|\#[xX](?<hex>[0-9a-fA-F]+));|:(?<emoji>[a-zA-Z0-9_#’-]+):",
		options: RegexOptions.Compiled
			| RegexOptions.IgnorePatternWhitespace
			| RegexOptions.ExplicitCapture);
	private static readonly Dictionary<string, string> EntityDictionary;
	private static readonly Dictionary<string, string> EmojiDictionary;

	/// <summary>
	/// Combines several inlines into one container inline.
	/// </summary>
	protected abstract TInline Span(IEnumerable<TInline> inlines);
	/// <summary>
	/// Creates a plain text inline; entities and emoji are already decoded.
	/// </summary>
	protected abstract TInline Run(string text);
	/// <summary>
	/// Creates a link around already-parsed <paramref name="content"/>.
	/// </summary>
	/// <param name="content">The link's label, parsed as markdown (nested links excluded).</param>
	/// <param name="target">The link destination.</param>
	/// <param name="tooltip">The quoted title, or <see langword="null"/> if none was given.</param>
	protected abstract TInline Hyperlink(TInline content, Uri target, string? tooltip);
	/// <summary>
	/// Creates an image inline.
	/// </summary>
	/// <param name="source">The image location.</param>
	/// <param name="altText">The alternative text from the <c>![alt]</c> label; the markdown syntax requires it, so it is only <see langword="null"/> for callers outside the parser.</param>
	/// <param name="tooltip">The quoted title, or <see langword="null"/> if none was given.</param>
	protected abstract TInline Image(Uri source, string? altText, string? tooltip);
	/// <summary>
	/// Makes <paramref name="content"/> bold, in place or by replacement.
	/// </summary>
	protected abstract void Embolden(ref TInline content);
	/// <summary>
	/// Makes <paramref name="content"/> italic, in place or by replacement.
	/// </summary>
	protected abstract void Italicize(ref TInline content);
	/// <summary>
	/// Lowers <paramref name="content"/> to subscript, in place or by replacement.
	/// </summary>
	protected abstract void Subscript(ref TInline content);
	/// <summary>
	/// Raises <paramref name="content"/> to superscript, in place or by replacement.
	/// </summary>
	protected abstract void Superscript(ref TInline content);

	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException">A disallowed element was encountered.</exception>
	public TInline ToInline(
			string markdown,
			MarkdownElementType disallowedElements = MarkdownElementType.None) {
		var inlines = ToInlines(markdown, disallowedElements);

		if (inlines.Count == 1) {
			return inlines[0];
		}

		return Span(inlines);
	}
	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException">A disallowed element was encountered.</exception>
	public IReadOnlyList<TInline> ToInlines(
			string markdown,
			MarkdownElementType disallowedElements = MarkdownElementType.None) {
		var enumerator = EnumerateInlines(markdown, disallowedElements);
		if (!enumerator.MoveNext()) {
			var ex = new InvalidOperationException("Expecting at least one inline.");
			logger.Error(ex, "MD:TOEMPTY");
			throw ex;
		}
		var first = enumerator.Current;
		if (!enumerator.MoveNext()) {
			return [first];
		}
		List<TInline> inlines = [first, enumerator.Current];
		while (enumerator.MoveNext()) {
			inlines.Add(enumerator.Current);
		}
		return inlines;
	}
	/// <inheritdoc/>
	/// <exception cref="InvalidOperationException">A disallowed element was encountered.</exception>
	public IEnumerator<TInline> EnumerateInlines(
			string markdown,
			MarkdownElementType disallowedElements = MarkdownElementType.None) {
		if (!MarkdownTextFormatPattern.TryMatch(markdown, out var match)) {
			yield return Run(DecodeText(markdown));
			yield break;
		}

		var lastIndex = 0;
		do {
			if (lastIndex < match.Index) {
				yield return Run(DecodeText(markdown[lastIndex..match.Index]));
			}

			// text-content patterns
			if (match.TryGetGroup("basic", out var basicGroup)) {
				var mark = basicGroup.Value;

				if (disallowedElements.HasFlag(MarkdownElementType.Basic)) {
					throw new InvalidOperationException($"basic element not allowed in this context! {disallowedElements}");
				}
				if (!match.TryGetGroup("text", out var group)) {
					var ex = new InvalidOperationException("Expecting textual content.");
					logger.Error(ex, "MD:TX:" + match.Value);
					throw ex;
				}
				var content = ToInline(group.Value, disallowedElements & ~MarkdownElementType.Basic);
				if (mark is "**" or "***") Embolden(ref content);
				if (mark is "*" or "***") Italicize(ref content);
				if (mark is "~") Subscript(ref content);
				if (mark is "^") Superscript(ref content);
				yield return content;
			}
			else if (match.TryGetGroup("url", out var urlGroup)) {
				var url = urlGroup.Value;
				if (match.TryGetGroup("image", out _)) {
					yield return DecodeImage(disallowedElements, match, url);
				}
				else {
					yield return DecodeHyperlink(disallowedElements, match, url);
				}
			}
			else {
				var ex = new InvalidOperationException("invalid markdown match?!");
				logger.Error(ex, "MD:INVALID:" + match.Value);
				throw ex;
			}

			lastIndex = match.Index + match.Length;
			match = match.NextMatch();
		} while (match.Success);

		// add any remaining text
		if (lastIndex != markdown.Length) {
			yield return Run(DecodeText(markdown[lastIndex..]));
		}
		yield break;
	}

	private TInline DecodeImage(MarkdownElementType disallowedElements, Match match, string url) {
		if (disallowedElements.HasFlag(MarkdownElementType.Image)) {
			var ex = new InvalidOperationException($"Image element not allowed in this context. {disallowedElements}");
			logger.Error(ex, "MD:IMG:" + url);
			throw ex;
		}
		string? toolTip = null;
		if (match.TryGetGroup("title", out var titleGroup)) {
			toolTip = DecodeText(titleGroup.Value);
		}
		string? altText = null;
		if (match.TryGetGroup("text", out var altGroup)) {
			altText = DecodeText(altGroup.Value);
		}
		return Image(new(url), altText, toolTip);
	}

	private TInline DecodeHyperlink(MarkdownElementType disallowedElements, Match match, string url) {
		TInline content;
		if (disallowedElements.HasFlag(MarkdownElementType.Hyperlink)) {
			var ex = new InvalidOperationException($"Hyperlink element not allowed in this context. {disallowedElements}");
			logger.Error(ex, "MD:HNEST:" + url);
			throw ex;
		}
		if (match.TryGetGroup("text", out var textGroup)) {
			content = ToInline(textGroup.Value, (disallowedElements | MarkdownElementType.Hyperlink) & ~MarkdownElementType.Basic);
		}
		else {
			content = Run(DecodeText(url));
		}
		string? toolTip = null;
		if (match.TryGetGroup("title", out var titleGroup)) {
			toolTip = DecodeText(titleGroup.Value);
		}
		return Hyperlink(content, new(url), toolTip);
	}

	private static string DecodeText(string text) {
		if (!EntitiesPattern.TryMatch(text, out var match)) {
			return text;
		}
		var sb = new StringBuilder();
		var lastIndex = 0;

		do {
			sb.Append(text, lastIndex, match.Index - lastIndex);
			lastIndex = match.Index + match.Length;
			if (match.TryGetGroup("entity", out var nameGroup)) {
				// the entity table is keyed by the full `&name;` form
				if (EntityDictionary?.TryGetValue($"&{nameGroup.Value};", out var entityText) is true) {
					sb.Append(entityText);
				}
				else {
					sb.Append(match.Value);
				}
			}
			else if (match.TryGetGroup("dec", out var decGroup)) {
				if (
					tryParseInt(decGroup, NumberStyles.None, out var codePoint)
					&& codePointAsText(codePoint) is string asText
				) {
					sb.Append(asText);
				}
				else {
					sb.Append(match.Value);
				}
			}
			else if (match.TryGetGroup("hex", out var hexGroup)) {
				if (
					tryParseInt(hexGroup, NumberStyles.AllowHexSpecifier, out var codePoint)
					&& codePointAsText(codePoint) is string asText
				) {
					sb.Append(asText);
				}
				else {
					sb.Append(match.Value);
				}
			}
			else if (match.TryGetGroup("emoji", out var emojiGroup)) {
				if (EmojiDictionary?.TryGetValue(emojiGroup.Value, out var emojiText) is true) {
					sb.Append(emojiText);
				}
				else {
					sb.Append(match.Value);
				}
			}
			else {
				throw new InvalidOperationException("name or number should have matches?!");
			}
			match = match.NextMatch();
		} while (match.Success);

		if (lastIndex < text.Length) {
			sb.Append(text, lastIndex, text.Length - lastIndex);
		}

		return sb.ToString();

		static bool tryParseInt(Capture capture, NumberStyles numberStyles, out int result) {
			return int.TryParse(capture.ValueSpan, numberStyles, CultureInfo.InvariantCulture, out result);
		}

		static string? codePointAsText(int codePoint) {
			if ((codePoint < 0 || codePoint >= 0xD800) && (codePoint <= 0xDFFF || codePoint > 0x10FFFF)) {
				return null;
			}

			return char.ConvertFromUtf32(codePoint);
		}
	}

	private class MarkdownConstants {
		[JsonPropertyName("entities")]
		public Dictionary<string, string>? EntityDictionary { get; set; }
		[JsonPropertyName("emojis")]
		public Dictionary<string, string>? EmojiDictionary { get; set; }
	}

	private static readonly string? resourceError;
	/// <summary>
	/// The logger handed to the constructor, never <see langword="null"/> (a discarding
	/// dummy stands in). Subclasses are welcome to report their own oddities through it.
	/// </summary>
	protected readonly ITakeException logger;

	/// <summary>
	/// Initializes the parser. The entity and emoji tables load once per process from
	/// an embedded resource; if that load failed, the failure is reported here as an
	/// <c>MD:INIT</c> warning and those patterns simply pass through undecoded.
	/// </summary>
	/// <param name="logger">Receives parse errors and the init warning; <see langword="null"/> discards them.</param>
	public MarkdownParser(ITakeException? logger) {
		this.logger = logger ?? ITakeException.Dummy;
		if (resourceError is not null) this.logger.Warn($"MD:INIT:{resourceError}");
	}
	static MarkdownParser() {
		Stream? stream = null;
		try {
			stream = typeof(Words).Assembly
				.GetManifestResourceStream(@"PatTech.Localization.Assets.markdown_constants.json");
			var json = new StreamReader(stream!)
				.ReadToEnd();
			var constants = JsonSerializer.Deserialize<MarkdownConstants>(json);
			EntityDictionary = constants?.EntityDictionary ?? [];
			EmojiDictionary = constants?.EmojiDictionary ?? [];
		}
		catch (Exception e) {
			resourceError = e.Message;
			EntityDictionary = [];
			EmojiDictionary = [];
		}
		finally {
			stream?.Dispose();
		}
	}
}

/// <summary>
/// Categories of inline markdown element, used to forbid elements that make no
/// sense in a given context (e.g. a hyperlink inside a hyperlink's own label).
/// </summary>
[Flags]
public enum MarkdownElementType {
	/// <summary>Nothing is disallowed.</summary>
	None = 0,
	/// <summary>Text styling: bold, italic, superscript and subscript.</summary>
	Basic = 1 << 0,
	/// <summary>Links, in both <c>[text](url)</c> and <c>&lt;url&gt;</c> forms.</summary>
	Hyperlink = 1 << 1,
	/// <summary>Images, i.e. <c>![alt](url)</c>.</summary>
	Image = 1 << 2,
}

/// <summary>
/// Converts inline markdown into framework-specific inline objects.
/// See <see cref="MarkdownParser{TInline}"/> for the supported syntax.
/// </summary>
/// <typeparam name="TInline">The framework's inline type, e.g. a WPF <c>Inline</c>.</typeparam>
public interface IMarkdownParser<TInline> {
	/// <summary>
	/// Parses <paramref name="markdown"/> into a single inline, wrapping multiple
	/// results in a span. Plain text with no markup comes back as a single run.
	/// </summary>
	/// <param name="markdown">The inline markdown text.</param>
	/// <param name="disallowedElements">Element categories that throw <see cref="InvalidOperationException"/> if encountered.</param>
	TInline ToInline(string markdown, MarkdownElementType disallowedElements = MarkdownElementType.None);
	/// <summary>
	/// Parses <paramref name="markdown"/> into the list of inlines it contains,
	/// in document order. Never empty: plain text yields one run, and the empty
	/// string yields one empty run.
	/// </summary>
	/// <param name="markdown">The inline markdown text.</param>
	/// <param name="disallowedElements">Element categories that throw <see cref="InvalidOperationException"/> if encountered.</param>
	IReadOnlyList<TInline> ToInlines(string markdown, MarkdownElementType disallowedElements = MarkdownElementType.None);
	/// <summary>
	/// Lazily parses <paramref name="markdown"/>, yielding each inline as it is
	/// read. Backs <see cref="ToInlines(string, MarkdownElementType)"/>.
	/// </summary>
	/// <param name="markdown">The inline markdown text.</param>
	/// <param name="disallowedElements">Element categories that throw <see cref="InvalidOperationException"/> if encountered.</param>
	IEnumerator<TInline> EnumerateInlines(string markdown, MarkdownElementType disallowedElements = MarkdownElementType.None);
}