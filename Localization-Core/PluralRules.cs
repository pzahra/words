using System;
using System.Collections.Generic;

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
		/// <summary>CLDR's six categories, in CLDR's order.</summary>
		public static IReadOnlyList<string> Names { get; } = ["zero", "one", "two", "few", "many", "other"];

		/// <summary>
		/// The category <paramref name="number"/> falls in for <paramref name="languageCode"/>:
		/// <c>"zero"</c>, <c>"one"</c>, <c>"two"</c>, <c>"few"</c>, <c>"many"</c> or <c>"other"</c>.
		/// The sign is ignored, a whole value counts as whole whatever its scale, and a
		/// fractional one is <c>other</c>.
		/// </summary>
		/// <param name="languageCode">A language, e.g. <c>"ru"</c>, <c>"pt-PT"</c> or a culture name; a region the table does not know falls to its language.</param>
		/// <param name="number">The count.</param>
		public static string Select(string languageCode, decimal number) {
			ArgumentNullException.ThrowIfNull(languageCode);
			number = Math.Abs(number);
			if (number != decimal.Truncate(number)) {
				return "other";
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
		/// handful of its words keep a dual, and its <c>many</c> reads <c>other</c>;
		/// Hebrew <c>two</c> reads <c>other</c>; the exact millions of French, Italian,
		/// Spanish, Portuguese and Catalan read <c>other</c>. Words' own table, not
		/// CLDR's; empty for most languages.
		/// </summary>
		/// <param name="languageCode">A language, as for <see cref="Select"/>.</param>
		public static IReadOnlyDictionary<string, string> Optional(string languageCode) {
			ArgumentNullException.ThrowIfNull(languageCode);
			return Lookup(optional, languageCode) ?? None;
		}

		private const decimal Large = 1_000_000_000_000m;

		private sealed record Rule(Func<long, string> Select, string[] Categories);

		private static Rule Find(string languageCode)
			=> languageCode == "" ? One : Lookup(rules, languageCode) ?? Other;

		//the language's entry, else its first segment's
		private static T? Lookup<T>(Dictionary<string, T> table, string languageCode) where T : class {
			string code = languageCode.Replace('_', '-');
			if (table.TryGetValue(code, out var entry)) {
				return entry;
			}
			int separator = code.IndexOf('-');
			return separator > 0 && table.TryGetValue(code[..separator], out entry) ? entry : null;
		}

		private static readonly Dictionary<string, string> None = [];

		private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> optional = Table<IReadOnlyDictionary<string, string>>(
			(new Dictionary<string, string> { ["two"] = "few", ["many"] = "other" }, "mt"),
			(new Dictionary<string, string> { ["two"] = "other" }, "he iw"),
			(new Dictionary<string, string> { ["many"] = "other" }, "ca es fr it lld pt scn vec"));

		private static bool Million(long i) => i != 0 && i % 1_000_000 == 0;

		private static readonly Rule Other = new(_ => "other", ["other"]);
		private static readonly Rule ZeroOne = new(i => i is 0 or 1 ? "one" : "other", ["one", "other"]);
		private static readonly Rule One = new(i => i == 1 ? "one" : "other", ["one", "other"]);
		private static readonly Rule OneMillions = new(i => i == 1 ? "one" : Million(i) ? "many" : "other", ["one", "many", "other"]);
		private static readonly Rule ZeroOneMillions = new(i => i is 0 or 1 ? "one" : Million(i) ? "many" : "other", ["one", "many", "other"]);
		private static readonly Rule Icelandic = new(i => i % 10 == 1 && i % 100 != 11 ? "one" : "other", ["one", "other"]);
		private static readonly Rule Filipino = new(i => i is 1 or 2 or 3 || i % 10 is not (4 or 6 or 9) ? "one" : "other", ["one", "other"]);
		private static readonly Rule Tamazight = new(i => i is 0 or 1 or (>= 11 and <= 99) ? "one" : "other", ["one", "other"]);
		private static readonly Rule Latvian = new(i => i % 10 == 0 || i % 100 is >= 11 and <= 19 ? "zero" : i % 10 == 1 ? "one" : "other", ["zero", "one", "other"]);
		private static readonly Rule ZeroOneOther = new(i => i == 0 ? "zero" : i == 1 ? "one" : "other", ["zero", "one", "other"]);
		private static readonly Rule OneTwo = new(i => i == 1 ? "one" : i == 2 ? "two" : "other", ["one", "two", "other"]);
		private static readonly Rule Tachelhit = new(i => i is 0 or 1 ? "one" : i is >= 2 and <= 10 ? "few" : "other", ["one", "few", "other"]);
		private static readonly Rule Romanian = new(i => i == 1 ? "one" : i == 0 || i % 100 is >= 1 and <= 19 ? "few" : "other", ["one", "few", "other"]);
		private static readonly Rule Bosnian = new(i => i % 10 == 1 && i % 100 != 11 ? "one"
			: i % 10 is >= 2 and <= 4 && i % 100 is not (>= 12 and <= 14) ? "few" : "other", ["one", "few", "other"]);
		private static readonly Rule Gaelic = new(i => i is 1 or 11 ? "one" : i is 2 or 12 ? "two"
			: i is (>= 3 and <= 10) or (>= 13 and <= 19) ? "few" : "other", ["one", "two", "few", "other"]);
		private static readonly Rule Slovenian = new(i => (i % 100) switch { 1 => "one", 2 => "two", 3 or 4 => "few", _ => "other" }, ["one", "two", "few", "other"]);
		private static readonly Rule Czech = new(i => i == 1 ? "one" : i is >= 2 and <= 4 ? "few" : "other", ["one", "few", "other"]);
		private static readonly Rule Polish = new(i => i == 1 ? "one"
			: i % 10 is >= 2 and <= 4 && i % 100 is not (>= 12 and <= 14) ? "few" : "many", ["one", "few", "many"]);
		private static readonly Rule Russian = new(i => i % 10 == 1 && i % 100 != 11 ? "one"
			: i % 10 is >= 2 and <= 4 && i % 100 is not (>= 12 and <= 14) ? "few" : "many", ["one", "few", "many"]);
		private static readonly Rule Lithuanian = new(i => i % 100 is >= 11 and <= 19 ? "other"
			: i % 10 == 1 ? "one" : i % 10 >= 2 ? "few" : "other", ["one", "few", "other"]);
		private static readonly Rule Breton = new(i => i % 10 == 1 && i % 100 is not (11 or 71 or 91) ? "one"
			: i % 10 == 2 && i % 100 is not (12 or 72 or 92) ? "two"
			: i % 10 is 3 or 4 or 9 && i % 100 is not ((>= 10 and <= 19) or (>= 70 and <= 79) or (>= 90 and <= 99)) ? "few"
			: Million(i) ? "many" : "other", ["one", "two", "few", "many", "other"]);
		private static readonly Rule Maltese = new(i => i == 1 ? "one" : i == 2 ? "two"
			: i == 0 || i % 100 is >= 3 and <= 10 ? "few" : i % 100 is >= 11 and <= 19 ? "many" : "other", ["one", "two", "few", "many", "other"]);
		private static readonly Rule Irish = new(i => i == 1 ? "one" : i == 2 ? "two"
			: i is >= 3 and <= 6 ? "few" : i is >= 7 and <= 10 ? "many" : "other", ["one", "two", "few", "many", "other"]);
		private static readonly Rule Manx = new(i => i % 10 == 1 ? "one" : i % 10 == 2 ? "two"
			: i % 100 is 0 or 20 or 40 or 60 or 80 ? "few" : "other", ["one", "two", "few", "other"]);
		private static readonly Rule Cornish = new(i => i == 0 ? "zero" : i == 1 ? "one"
			: i % 100 is 2 or 22 or 42 or 62 or 82
				|| i % 1000 == 0 && i % 100_000 is (>= 1000 and <= 20_000) or 40_000 or 60_000 or 80_000
				|| i % 1_000_000 == 100_000 ? "two"
			: i % 100 is 3 or 23 or 43 or 63 or 83 ? "few"
			: i % 100 is 1 or 21 or 41 or 61 or 81 ? "many" : "other", ["zero", "one", "two", "few", "many", "other"]);
		private static readonly Rule Arabic = new(i => i == 0 ? "zero" : i == 1 ? "one" : i == 2 ? "two"
			: i % 100 is >= 3 and <= 10 ? "few" : i % 100 >= 11 ? "many" : "other", ["zero", "one", "two", "few", "many", "other"]);
		private static readonly Rule Welsh = new(i => i switch { 0 => "zero", 1 => "one", 2 => "two", 3 => "few", 6 => "many", _ => "other" },
			["zero", "one", "two", "few", "many", "other"]);

		private static readonly Dictionary<string, Rule> rules = Table(
			(Other, "bm bo dz hnj id ig ii in ja jbo jv jw kde kea km ko lkt lo ms my nqo osa sah ses sg su th to tpi vi wo yo yue zh"),
			(ZeroOne, "ak am as bho bn csw doi fa ff gu guw hi hy kab kn ln mg nso pa pcm si ti wa zu"),
			(One, "af an asa ast az bal bem bez bg brx ce cgg chr ckb da de dv ee el en eo et eu fi fo fur fy gl gsw ha haw hu ia ie io jgo ji jmc "
				+ "ka kaj kcg kk kkj kl ks ksb ku ky lb lg lij mas mgo ml mn mr nah nb nd ne nl nn nnh no nr ny nyn om or os pap ps rm rof rwk "
				+ "saq sc sd sdh seh sn so sq ss ssy st sv sw syr ta te teo tig tk tn tr ts ug ur uz ve vo vun wae xh xog yi"),
			(OneMillions, "ca es it lld pt-PT scn vec"),
			(ZeroOneMillions, "fr pt"),
			(Icelandic, "is mk"),
			(Filipino, "ceb fil tl"),
			(Tamazight, "tzm"),
			(Latvian, "lv prg"),
			(ZeroOneOther, "ksh lag"),
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
