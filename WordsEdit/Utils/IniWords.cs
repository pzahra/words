using PatTech.Text.Ini;

namespace WordsEdit.Utils;

/// <summary>
///     The ini driver's failures in Wordsmith's own words (SPEC: Ini files through the driver): the driver
///     has none of its own, only a type and its data. Anything else a file throws says itself.
/// </summary>
public static class IniWords {
	/// <summary>What went wrong with a file, as the <c>{1}</c> of <c>file.write-failed</c> and its kin say it.</summary>
	public static string Say(Exception failure) => failure switch {
		IniCommitException { Failure: IniCommitFailure.Changed } => Words.Known["ini.changed"],
		IniCommitException { Failure: IniCommitFailure.Held } => Words.Known["ini.held"],
		IniCommitException { Failure: IniCommitFailure.Denied } => Words.Known["ini.denied"],
		IniCommitException { Failure: IniCommitFailure.Running } => Words.Known["ini.running"],
		IniCommitException { Failure: IniCommitFailure.Unsettled, Kept: { } kept } refused => Words.Known.Format("ini.unsettled-kept", refused.Log ?? "", Reason(refused), kept),
		IniCommitException { Failure: IniCommitFailure.Unsettled } refused => Words.Known.Format("ini.unsettled", refused.Log ?? "", Reason(refused)),
		IniCommitException { Failure: IniCommitFailure.LogWrite } refused => Words.Known.Format("ini.log-write", Reason(refused)),
		IniPendingException pending => Words.Known.Format("ini.pending", pending.Log),
		IniCheckException => Words.Known["ini.check"],
		IniEncodingException { Failure: IniEncodingFailure.MarkRefused } refused => Words.Known.Format("ini.mark-refused", refused.Encoding ?? ""),
		IniEncodingException { Failure: IniEncodingFailure.Unencodable } refused => Words.Known.Format("ini.unencodable", refused.Encoding ?? ""),
		IniEncodingException => Words.Known["ini.not-text"],
		IniEditException refused => Words.Known.Format("ini.unwritable", refused.Name ?? ""),
		_ => failure.Message,
	};

	//what the system said when the log would not write, or would not settle
	private static string Reason(IniException failure) => failure.InnerException?.Message ?? "";
}
