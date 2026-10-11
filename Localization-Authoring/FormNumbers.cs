using PatTech.Localization;
using System.Collections.Concurrent;
using System.Globalization;

namespace PatTech.Localization.Authoring {
	/// <summary>
	///     The counts a category takes in a language: the editor's form selector
	///     reads them in its own culture's digits, the pick that follows takes a
	///     sample, and a translation request says them in invariant digits (editor
	///     SPEC: Plural forms, Machine translation).
	/// </summary>
	public static class FormNumbers {
		//the counts looked at: every one up to here, then exact millions for the categories that start there
		private const int Scan = 1000;
		private const int Shown = 4;
		//the rules never change, and the menus re-read every row on each refresh
		private static readonly ConcurrentDictionary<(string Culture, string Language, string Category), string> described = [];

		/// <summary>The runs of counts <paramref name="category"/> takes: <c>0, 3–10, 103–110…</c>, in <paramref name="culture"/>'s digits, the current culture's when it is null.</summary>
		public static string Describe(string language, string category, CultureInfo? culture = null) {
			culture ??= CultureInfo.CurrentCulture;
			return described.GetOrAdd((culture.Name, language, category), at => Runs(at.Language, at.Category, culture));
		}

		private static string Runs(string language, string category, CultureInfo culture) {
			List<(long From, long To)> runs = [];
			for (int n = 0; n <= Scan; n++) {
				if (PluralRules.Select(language, n) != category) {
					continue;
				}
				if (runs.Count > 0 && runs[^1].To == n - 1) {
					runs[^1] = (runs[^1].From, n);
				}
				else {
					runs.Add((n, n));
				}
			}
			if (runs.Count == 0) {
				runs.AddRange(Millions().Where(n => PluralRules.Select(language, n) == category).Select(n => (n, n)));
			}
			var shown = runs.Take(Shown).Select(run => run.To == Scan ? $"{Number(run.From)}…"
				: run.From == run.To ? Number(run.From) : $"{Number(run.From)}–{Number(run.To)}");
			//an open run already trails off, and only the last run can be open
			bool more = runs.Count > Shown || runs.Count > 0 && runs[^1].From >= 1_000_000;
			return string.Join(", ", shown) + (more ? "…" : "");

			string Number(long n) => n.ToString("N0", culture);
		}

		/// <summary>A count <paramref name="category"/> takes: the first from one up, else zero, else a million; null for none.</summary>
		public static long? Sample(string language, string category) {
			for (int n = 1; n <= Scan; n++) {
				if (PluralRules.Select(language, n) == category) {
					return n;
				}
			}
			if (PluralRules.Select(language, 0) == category) {
				return 0;
			}
			return Millions().Cast<long?>().FirstOrDefault(n => PluralRules.Select(language, n!.Value) == category);
		}

		private static IEnumerable<long> Millions() => [1_000_000, 2_000_000, 3_000_000];
	}
}
