using PatTech.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace PatTech.Localization {
	/// <summary>
	/// A language code as a <c>words.ini</c> writes it (SPEC: Language codes): the BCP 47
	/// subset <c>language(-Script)?(-REGION)?</c>, where the language is 2 or 3 letters,
	/// the script 4 letters, and the region 2 letters or 3 digits — <c>en</c>, <c>ceb</c>,
	/// <c>es-419</c>, <c>zh-Hans-CN</c>, <c>sr-Latn-RS</c>. Each subtag is cased by its
	/// kind (<c>sr-latn-rs</c> reads <c>sr-Latn-RS</c>), so two codes compare as their
	/// <see cref="ToString"/>. A code falls back by truncation, <c>zh-Hant-TW</c> to
	/// <c>zh-Hant</c> to <c>zh</c> (<see cref="Chain"/>). The <see langword="default"/>
	/// value is no code, and reads as the empty string, the language-less default.
	/// </summary>
	public readonly struct LanguageCode : IEquatable<LanguageCode> {
		private static readonly Regex rxCode = new(
			@"^(?<lang>[a-zA-Z]{2,3})(-(?<script>[a-zA-Z]{4}))?(-(?<region>[a-zA-Z]{2}|[0-9]{3}))?\z",
			RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant);

		private readonly string? code, language, script, region;

		/// <summary>The language subtag, lowercase: <c>zh</c> in <c>zh-Hant-TW</c>.</summary>
		public string Language => language ?? "";
		/// <summary>The script subtag, titlecase, or empty: <c>Hant</c> in <c>zh-Hant-TW</c>.</summary>
		public string Script => script ?? "";
		/// <summary>The region subtag, uppercase or 3 digits, or empty: <c>TW</c> in <c>zh-Hant-TW</c>.</summary>
		public string Region => region ?? "";

		private LanguageCode(string language, string script, string region) {
			this.language = language;
			this.script = script;
			this.region = region;
			code = language + (script == "" ? "" : "-" + script) + (region == "" ? "" : "-" + region);
		}

		/// <summary>
		/// Reads <paramref name="text"/> as a code, in any casing; whether it is one.
		/// The empty string is none: it names the default, which has no code.
		/// </summary>
		/// <param name="text">The code as written, e.g. <c>"EN-gb"</c>.</param>
		/// <param name="code">The code, cased by kind; <see langword="default"/> when it is none.</param>
		public static bool TryParse([NotNullWhen(true)] string? text, out LanguageCode code) {
			if (text is null || !rxCode.TryMatch(text, out var match)) {
				code = default;
				return false;
			}
			string script = match.Groups["script"].Value;
			code = new LanguageCode(
				match.Groups["lang"].Value.ToLowerInvariant(),
				script == "" ? "" : char.ToUpperInvariant(script[0]) + script[1..].ToLowerInvariant(),
				match.Groups["region"].Value.ToUpperInvariant());
			return true;
		}

		/// <summary>
		/// Reads a culture's name, which can say more than a code does (<c>ca-ES-valencia</c>,
		/// <c>en-US-POSIX</c>, <c>de-DE_phoneb</c>): the longest run of its leading subtags
		/// that is a code, so <c>ca-ES-valencia</c> reads <c>ca-ES</c>. A name whose first
		/// subtag is no language reads as none.
		/// </summary>
		/// <param name="name">A code or a culture's name, e.g. <c>CultureInfo.CurrentUICulture.Name</c>.</param>
		/// <param name="code">The code it reads as; <see langword="default"/> when none.</param>
		public static bool TryRead([NotNullWhen(true)] string? name, out LanguageCode code) {
			string? text = name?.Replace('_', '-');
			while (text is { Length: > 0 }) {
				if (TryParse(text, out code)) {
					return true;
				}
				int separator = text.LastIndexOf('-');
				text = separator > 0 ? text[..separator] : null;
			}
			code = default;
			return false;
		}

		/// <summary>Whether this is a code: <see langword="false"/> for the <see langword="default"/> value.</summary>
		public bool IsCode => code is not null;

		/// <summary>
		/// This code and each shorter one it falls back to, longest first: <c>zh-Hant-TW</c>,
		/// <c>zh-Hant</c>, <c>zh</c>. A region falls back past its script, never to a
		/// sibling, so Traditional never reads Simplified. Empty for the <see langword="default"/> value.
		/// </summary>
		public IEnumerable<LanguageCode> Chain {
			get {
				if (code is null) {
					yield break;
				}
				yield return this;
				if (Region != "" && Script != "") {
					yield return new LanguageCode(Language, Script, "");
				}
				if (Region != "" || Script != "") {
					yield return new LanguageCode(Language, "", "");
				}
			}
		}

		/// <summary>
		/// Whether <paramref name="prefix"/> is this code or one it falls back to
		/// (<see cref="Chain"/>): <c>zh</c> and <c>zh-Hant</c> both are for <c>zh-Hant-TW</c>,
		/// <c>zh-TW</c> is not.
		/// </summary>
		/// <param name="prefix">The shorter code.</param>
		public bool StartsWith(LanguageCode prefix) {
			foreach (var shorter in Chain) {
				if (shorter == prefix) {
					return true;
				}
			}
			return false;
		}

		/// <summary>The code as a file writes it, cased by kind: <c>zh-Hant-TW</c>; the empty string for the <see langword="default"/> value.</summary>
		public override string ToString() => code ?? "";

		/// <summary>Equal when both are the same code, or both none.</summary>
		public bool Equals(LanguageCode other) => code == other.code;
		/// <summary>Equal when <paramref name="obj"/> is the same <see cref="LanguageCode"/>.</summary>
		public override bool Equals(object? obj) => obj is LanguageCode other && Equals(other);
		/// <summary>The hash of the code's text.</summary>
		public override int GetHashCode() => code?.GetHashCode() ?? 0;
		/// <summary>Whether two codes are the same.</summary>
		public static bool operator ==(LanguageCode lhs, LanguageCode rhs) => lhs.Equals(rhs);
		/// <summary>Whether two codes differ.</summary>
		public static bool operator !=(LanguageCode lhs, LanguageCode rhs) => !lhs.Equals(rhs);
	}
}
