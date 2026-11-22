using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PatTech.Localization {
	/// <summary>
	/// Unicode CLDR's plural rules for whole numbers, carried as a table since .NET
	/// exposes none (SPEC: Plural forms): which of the six categories a count falls in,
	/// language by language, following the current CLDR release — so French, Italian
	/// and Spanish count exact millions as <c>many</c>. Fractions wait: a number with a
	/// fractional part is <c>other</c>. A language the table does not know has the one
	/// category <c>other</c>, as CLDR's root does; the invariant culture (<c>""</c>)
	/// counts as English.
	/// </summary>
	public static class PluralRules {
		//CLDR's categories by the names a file writes after a #, so a misspelt one is
		//a compile error rather than a form nothing ever picks
		internal const string Zero = "zero";
		internal const string One = "one";
		internal const string Two = "two";
		internal const string Few = "few";
		internal const string Many = "many";
		internal const string Other = "other";

		/// <summary>CLDR's six categories, in CLDR's order.</summary>
		public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly<string>([Zero, One, Two, Few, Many, Other]);

		/// <summary>
		/// The category <paramref name="number"/> falls in for <paramref name="languageCode"/>:
		/// <c>"zero"</c>, <c>"one"</c>, <c>"two"</c>, <c>"few"</c>, <c>"many"</c> or <c>"other"</c>.
		/// The sign is ignored, a whole value counts as whole whatever its scale, and a
		/// fractional one is <c>other</c>.
		/// </summary>
		/// <param name="languageCode">A language, e.g. <c>"ru"</c>, <c>"pt-PT"</c> or a culture name; a code the table does not know falls back to a shorter one, as <see cref="LanguageCode.Chain"/> does.</param>
		/// <param name="number">The count.</param>
		public static string Select(string languageCode, decimal number) {
			ArgumentNullException.ThrowIfNull(languageCode);
			number = Math.Abs(number);
			if (number != decimal.Truncate(number)) {
				return Other;
			}
			//past 10^12 only the low digits matter to any rule, and the count stays large
			long i = number <= Large ? (long)number : (long)(number % Large) + (long)Large;
			return Find(languageCode).Select(i);
		}

		/// <summary>
		/// The categories a whole number reaches in <paramref name="languageCode"/>, in
		/// CLDR's order: <c>one, other</c> for English, <c>one, few, many</c> for Russian
		/// (whose <c>other</c> takes only fractions), just <c>other</c> for Japanese.
		/// </summary>
		/// <param name="languageCode">A language, as for <see cref="Select"/>.</param>
		public static IReadOnlyList<string> Categories(string languageCode) {
			ArgumentNullException.ThrowIfNull(languageCode);
			return Find(languageCode).Categories;
		}

		/// <summary>
		/// The categories a translation into <paramref name="languageCode"/> may leave
		/// out, each with the category a missing form reads instead: the ones that
		/// usually read like another. Maltese <c>two</c> reads <c>few</c>, since only a
		/// handful of its words keep a dual, its <c>many</c> reads <c>other</c>, and its
		/// <c>other</c> reads <c>one</c>, the plain value: from 11 up a Maltese count
		/// takes the singular. Hebrew <c>two</c> reads <c>other</c>; the exact millions
		/// of French, Italian, Spanish, Portuguese and Catalan, of Ladin, Sicilian and
		/// Venetian, which share the rule, and of Breton, whose <c>many</c> is the same,
		/// read <c>other</c>. Words' own table, not CLDR's; empty for most languages.
		/// </summary>
		/// <param name="languageCode">A language, as for <see cref="Select"/>.</param>
		public static IReadOnlyDictionary<string, string> Optional(string languageCode) {
			ArgumentNullException.ThrowIfNull(languageCode);
			return Lookup(optional, languageCode) ?? None;
		}

		private const decimal Large = 1_000_000_000_000m;

		private sealed class Rule(Func<long, string> select, string[] categories) {
			public Func<long, string> Select { get; } = select;
			//read-only: every caller of Categories shares it
			public IReadOnlyList<string> Categories { get; } = Array.AsReadOnly(categories);
		}

		private static Rule Find(string languageCode)
			=> languageCode == "" ? OneOnly : Lookup(rules, languageCode) ?? OtherOnly;

		//the language's entry, else that of each shorter code it falls back to, as
		//LanguageCode.Chain does: zh-Hant-TW, zh-Hant, zh; a culture's name truncates alike
		private static T? Lookup<T>(Dictionary<string, T> table, string languageCode) where T : class {
			string? code = languageCode.Replace('_', '-');
			while (code is { Length: > 0 }) {
				if (table.TryGetValue(code, out var entry)) {
					return entry;
				}
				int separator = code.LastIndexOf('-');
				code = separator > 0 ? code[..separator] : null;
			}
			return null;
		}

		private static readonly IReadOnlyDictionary<string, string> None = ReadOnlyDictionary<string, string>.Empty;

		private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> optional = Table<IReadOnlyDictionary<string, string>>(
			(new Dictionary<string, string> { [Two] = Few, [Many] = Other, [Other] = One }.AsReadOnly(), "mt"),
			(new Dictionary<string, string> { [Two] = Other }.AsReadOnly(), "he iw"),
			(new Dictionary<string, string> { [Many] = Other }.AsReadOnly(), "br ca es fr it lld pt scn vec"));

		private static bool Million(long i) => i != 0 && i % 1_000_000 == 0;

		private static readonly Rule OtherOnly = new(_ => Other, [Other]);
		private static readonly Rule ZeroOne = new(i => i is 0 or 1 ? One : Other, [One, Other]);
		private static readonly Rule OneOnly = new(i => i == 1 ? One : Other, [One, Other]);
		private static readonly Rule OneMillions = new(i => i == 1 ? One : Million(i) ? Many : Other, [One, Many, Other]);
		private static readonly Rule ZeroOneMillions = new(i => i is 0 or 1 ? One : Million(i) ? Many : Other, [One, Many, Other]);
		private static readonly Rule Icelandic = new(i => i % 10 == 1 && i % 100 != 11 ? One : Other, [One, Other]);
		private static readonly Rule Filipino = new(i => i is 1 or 2 or 3 || i % 10 is not (4 or 6 or 9) ? One : Other, [One, Other]);
		private static readonly Rule Tamazight = new(i => i is 0 or 1 or (>= 11 and <= 99) ? One : Other, [One, Other]);
		private static readonly Rule Latvian = new(i => i % 10 == 0 || i % 100 is >= 11 and <= 19 ? Zero : i % 10 == 1 ? One : Other, [Zero, One, Other]);
		private static readonly Rule ZeroOneOther = new(i => i == 0 ? Zero : i == 1 ? One : Other, [Zero, One, Other]);
		private static readonly Rule OneTwo = new(i => i == 1 ? One : i == 2 ? Two : Other, [One, Two, Other]);
		private static readonly Rule Tachelhit = new(i => i is 0 or 1 ? One : i is >= 2 and <= 10 ? Few : Other, [One, Few, Other]);
		private static readonly Rule Romanian = new(i => i == 1 ? One : i == 0 || i % 100 is >= 1 and <= 19 ? Few : Other, [One, Few, Other]);
		private static readonly Rule Bosnian = new(i => i % 10 == 1 && i % 100 != 11 ? One
			: i % 10 is >= 2 and <= 4 && i % 100 is not (>= 12 and <= 14) ? Few : Other, [One, Few, Other]);
		private static readonly Rule Gaelic = new(i => i is 1 or 11 ? One : i is 2 or 12 ? Two
			: i is (>= 3 and <= 10) or (>= 13 and <= 19) ? Few : Other, [One, Two, Few, Other]);
		private static readonly Rule Slovenian = new(i => (i % 100) switch { 1 => One, 2 => Two, 3 or 4 => Few, _ => Other }, [One, Two, Few, Other]);
		private static readonly Rule Czech = new(i => i == 1 ? One : i is >= 2 and <= 4 ? Few : Other, [One, Few, Other]);
		private static readonly Rule Polish = new(i => i == 1 ? One
			: i % 10 is >= 2 and <= 4 && i % 100 is not (>= 12 and <= 14) ? Few : Many, [One, Few, Many]);
		private static readonly Rule Russian = new(i => i % 10 == 1 && i % 100 != 11 ? One
			: i % 10 is >= 2 and <= 4 && i % 100 is not (>= 12 and <= 14) ? Few : Many, [One, Few, Many]);
		private static readonly Rule Lithuanian = new(i => i % 100 is >= 11 and <= 19 ? Other
			: i % 10 == 1 ? One : i % 10 >= 2 ? Few : Other, [One, Few, Other]);
		private static readonly Rule Samogitian = new(i => i % 10 == 1 && i % 100 != 11 ? One : i == 2 ? Two
			: i % 10 >= 2 && i % 100 is not (>= 11 and <= 19) ? Few : Other, [One, Two, Few, Other]);
		private static readonly Rule Breton = new(i => i % 10 == 1 && i % 100 is not (11 or 71 or 91) ? One
			: i % 10 == 2 && i % 100 is not (12 or 72 or 92) ? Two
			: i % 10 is 3 or 4 or 9 && i % 100 is not ((>= 10 and <= 19) or (>= 70 and <= 79) or (>= 90 and <= 99)) ? Few
			: Million(i) ? Many : Other, [One, Two, Few, Many, Other]);
		private static readonly Rule Maltese = new(i => i == 1 ? One : i == 2 ? Two
			: i == 0 || i % 100 is >= 3 and <= 10 ? Few : i % 100 is >= 11 and <= 19 ? Many : Other, [One, Two, Few, Many, Other]);
		private static readonly Rule Irish = new(i => i == 1 ? One : i == 2 ? Two
			: i is >= 3 and <= 6 ? Few : i is >= 7 and <= 10 ? Many : Other, [One, Two, Few, Many, Other]);
		private static readonly Rule Manx = new(i => i % 10 == 1 ? One : i % 10 == 2 ? Two
			: i % 100 is 0 or 20 or 40 or 60 or 80 ? Few : Other, [One, Two, Few, Other]);
		private static readonly Rule Cornish = new(i => i == 0 ? Zero : i == 1 ? One
			: i % 100 is 2 or 22 or 42 or 62 or 82
				|| i % 1000 == 0 && i % 100_000 is (>= 1000 and <= 20_000) or 40_000 or 60_000 or 80_000
				|| i % 1_000_000 == 100_000 ? Two
			: i % 100 is 3 or 23 or 43 or 63 or 83 ? Few
			: i % 100 is 1 or 21 or 41 or 61 or 81 ? Many : Other, [Zero, One, Two, Few, Many, Other]);
		private static readonly Rule Arabic = new(i => i == 0 ? Zero : i == 1 ? One : i == 2 ? Two
			: i % 100 is >= 3 and <= 10 ? Few : i % 100 >= 11 ? Many : Other, [Zero, One, Two, Few, Many, Other]);
		private static readonly Rule Welsh = new(i => i switch { 0 => Zero, 1 => One, 2 => Two, 3 => Few, 6 => Many, _ => Other },
			[Zero, One, Two, Few, Many, Other]);

		private static readonly Dictionary<string, Rule> rules = Table(
			(OtherOnly, "bm bo dz hnj id ig ii in ja jbo jv jw kde kea km ko lkt lo ms my nqo osa sah ses sg su th to tpi vi wo yo yue zh"),
			(ZeroOne, "ak am as bho bn csw doi fa ff gu guw hi hy kab kn kok ln mg nso pa pcm si ti wa zu"),
			(OneOnly, "af an asa ast az bal bem bez bg brx ce cgg chr ckb da de dv ee el en eo et eu fi fo fur fy gl gsw ha haw hu ia ie io jgo ji jmc "
				+ "ka kaj kcg kk kkj kl ks ksb ku ky lb lg lij mas mgo ml mn mr nah nb nd ne nl nn nnh no nr ny nyn om or os pap ps rm rof rwk "
				+ "saq sc sd sdh seh sn so sq ss ssy st sv sw syr ta te teo tig tk tn tr ts ug ur uz ve vo vun wae xh xog yi"),
			(OneMillions, "ca es it lld pt-PT scn vec"),
			(ZeroOneMillions, "fr pt"),
			(Icelandic, "is mk"),
			(Filipino, "ceb fil tl"),
			(Tamazight, "tzm"),
			(Latvian, "lv prg"),
			(ZeroOneOther, "blo cv ksh lag"),
			(OneTwo, "he iu iw naq sat se sma smi smj smn sms"),
			(Tachelhit, "shi"),
			(Romanian, "mo ro"),
			(Bosnian, "bs hr sh sr"),
			(Gaelic, "gd"),
			(Slovenian, "dsb hsb sl"),
			(Czech, "cs sk"),
			(Polish, "pl"),
			(Russian, "be ru uk"),
			(Lithuanian, "lt"),
			(Samogitian, "sgs"),
			(Breton, "br"),
			(Maltese, "mt"),
			(Irish, "ga"),
			(Manx, "gv"),
			(Cornish, "kw"),
			(Arabic, "ar ars"),
			(Welsh, "cy"));

		private static Dictionary<string, T> Table<T>(params (T Entry, string Codes)[] groups) {
			var table = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
			foreach (var (entry, codes) in groups) {
				foreach (string code in codes.Split(' ')) {
					table.Add(code, entry);
				}
			}
			return table;
		}
	}
}
