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

public abstract class MarkdownParser<TInline> : IMarkdownParser<TInline> {
	private static readonly Regex MarkdownTextFormatPattern = new(
		pattern: @"
# italic, bold, or both using `*`, `**`, or `***` respectively
 (?<basic>\*{1,3})(?=[\S-[*]])(?<text>.+?)(?<=[\S-[*]])\k<basic>
|(?<basic>\^)(?=[\S-[\^]])(?<text>.+?)(?<=[\S-[\^]])\k<basic>
|(?<basic>~)(?=[\S-[~]])(?<text>.+?)(?<=[\S-[~]])\k<basic>
# full link (or image) `[text](url)` or `[label](url ""title"")`
|(?<image>!)\[(?<text>[^]]+)\]\(\s*(?<url>[^\s)]+)(\s+""(?<title>[^""]*)"")?\s*\)
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

	protected abstract TInline Span(IEnumerable<TInline> inlines);
	protected abstract TInline Run(string text);
	protected abstract TInline Hyperlink(TInline content, Uri target, string? tooltip);
	protected abstract TInline Image(Uri source, string? altText, string? tooltip);
	protected abstract void Embolden(ref TInline content);
	protected abstract void Italicize(ref TInline content);
	protected abstract void Subscript(ref TInline content);
	protected abstract void Superscript(ref TInline content);

	public TInline ToInline(
			string markdown,
			MarkdownElementType disallowedElements = MarkdownElementType.None) {
		var inlines = ToInlines(markdown, disallowedElements);

		if (inlines.Count == 1) {
			return inlines[0];
		}

		return Span(inlines);
	}
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
				if (match.Groups.ContainsKey("image")) {
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
		if (match.TryGetGroup("content", out var altGroup)) {
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
				if (EntityDictionary?.TryGetValue(nameGroup.Value, out var entityText) is true) {
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
	private readonly ITakeException logger;

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

[Flags]
public enum MarkdownElementType {
	None = 0,
	Basic = 1 << 0,
	Hyperlink = 1 << 1,
	Image = 1 << 2,
}

public interface IMarkdownParser<TInline> {
	TInline ToInline(string markdown, MarkdownElementType disallowedElements = MarkdownElementType.None);
	IReadOnlyList<TInline> ToInlines(string markdown, MarkdownElementType disallowedElements = MarkdownElementType.None);
	IEnumerator<TInline> EnumerateInlines(string markdown, MarkdownElementType disallowedElements = MarkdownElementType.None);
}