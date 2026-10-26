using System.Text;
using Xunit;

namespace PatTech.Text.Ini.Tests;

/// <summary>
///     Recovery (SPEC: Recovery), from each log by hand and from a process killed at each step: a
///     log never whole goes, the file untouched; one over a file written goes; one over a file at
///     its hash before writes it, and makes a new file; a file at neither hash is kept first,
///     never over another copy; a recovery killed partway is finished by the next; a log still
///     held refuses the open, and one that will not settle is kept.
/// </summary>
[Collection("files")]
public sealed class RecoveryTests : IDisposable {
	//a value long enough that a write broken off halfway leaves the file at neither hash
	private const string Value = "22222222222";
	private const string Before = "[s]\nx=1\ny=3\n";
	private const string After = "[s]\nx=" + Value + "\ny=3\n";
	private readonly Folder folder = new();

	public void Dispose() => folder.Dispose();

	private IniTransaction Open(string path) => IniTransaction.Open(path, Folder.Dialect);

	//a log as a commit leaves it, before as the file was, null for none
	private void Log(string path, string? before, string after, int cut = 0) {
		using (var log = new FileStream(IniLog.PathOf(path), FileMode.CreateNew, FileAccess.ReadWrite)) {
			IniLog.Write(log, before is null ? null : IniFiles.Hash(Encoding.UTF8.GetBytes(before)), Encoding.UTF8.GetBytes(after));
			log.SetLength(log.Length - cut);
		}
	}

	[Fact]
	public void ALogWithoutItsTrailer_Goes_AndTheFileIsUntouched() {
		string path = folder.Write("a.ini", Before);
		Log(path, Before, After, cut: 3);
		using (IniTransaction file = Open(path)) {
			Assert.Equal(new IniRecovery(IniRecoveryKind.Unwhole, null), file.Recovery);
			Assert.Equal("1", file.Get("s", "x"));
		}
		Assert.Equal(["a.ini"], folder.Names);
	}

	[Fact]
	public void AWholeLog_OverAFileAlreadyWritten_Goes() {
		string path = folder.Write("a.ini", After);
		Log(path, Before, After);
		using (IniTransaction file = Open(path)) {
			Assert.Equal(new IniRecovery(IniRecoveryKind.Finished, null), file.Recovery);
		}
		Assert.Equal(After, Folder.Read(path));
		Assert.Equal(["a.ini"], folder.Names);
	}

	[Fact]
	public void AWholeLog_OverAFileAtItsHashBefore_WritesIt_AndOverANewFileNeverMade_MakesIt() {
		string path = folder.Write("a.ini", Before);
		Log(path, Before, After);
		using (IniTransaction file = Open(path)) {
			Assert.Equal(new IniRecovery(IniRecoveryKind.Replayed, null), file.Recovery);
			Assert.Equal(Value, file.Get("s", "x"));
		}
		string made = folder["new.ini"];
		Log(made, null, After);
		using (IniTransaction file = Open(made)) {
			Assert.Equal(IniRecoveryKind.Replayed, file.Recovery?.Kind);
		}
		Assert.Equal(After, Folder.Read(made));
		Assert.Equal(["a.ini", "new.ini"], folder.Names);
	}

	[Fact]
	public void AFileAtNeitherHash_IsKeptFirst_ASecondCopyNeverOverTheFirst() {
		string path = folder.Write("a.ini", "[s]\nx=mine\n");
		Log(path, Before, After);
		using (IniTransaction file = Open(path)) {
			Assert.Equal(new IniRecovery(IniRecoveryKind.Kept, folder[".a.ini.recovered"]), file.Recovery);
		}
		Assert.Equal(After, Folder.Read(path));
		Assert.Equal("[s]\nx=mine\n", Folder.Read(folder[".a.ini.recovered"]));

		folder.Write("a.ini", "[s]\nx=theirs\n");
		Log(path, Before, After);
		using (IniTransaction file = Open(path)) {
			Assert.Equal(folder[".a.ini.recovered-2"], file.Recovery?.Kept);
		}
		Assert.Equal("[s]\nx=mine\n", Folder.Read(folder[".a.ini.recovered"]));
		Assert.Equal("[s]\nx=theirs\n", Folder.Read(folder[".a.ini.recovered-2"]));
		Assert.Equal([".a.ini.recovered", ".a.ini.recovered-2", "a.ini"], folder.Names);
	}

	[Fact]
	public void AWholeLog_OverAFileAlreadyWrittenAndNowReadOnly_Goes_TheFileOnlyRead() {
		string path = folder.Write("a.ini", After);
		File.SetAttributes(path, FileAttributes.ReadOnly);
		Log(path, Before, After);
		using (IniTransaction file = Open(path)) {
			Assert.Equal(new IniRecovery(IniRecoveryKind.Finished, null), file.Recovery);
		}
		Assert.Equal(["a.ini"], folder.Names);
	}

	[Fact]
	public void ARecoveryThatKeepsTheBytes_AndThenFailsToWrite_SaysWhereTheyWereKept() {
		string path = folder.Write("a.ini", "[s]\nx=mine\n");
		Log(path, Before, After);
		IniFiles.Step = step => {
			if (step == "settling") {
				throw new IOException("the disk is full");
			}
		};
		IniCommitException refused = Assert.Throws<IniCommitException>(() => Open(path));
		IniFiles.Step = null;
		Assert.Equal((IniCommitFailure.Unsettled, folder[".a.ini.recovered"]), (refused.Failure, refused.Kept));
		Assert.Equal("[s]\nx=mine\n", Folder.Read(folder[".a.ini.recovered"]));
		Assert.Contains(".a.ini.tmp", folder.Names);
	}

	[Fact]
	public void AKeptCopyLeftHalfWritten_Goes() {
		string path = folder.Write("a.ini", "[s]\nx=mine\n");
		folder.Write(".a.ini.recovered.tmp", "half");
		Log(path, Before, After);
		using (Open(path)) {
		}
		Assert.Equal([".a.ini.recovered", "a.ini"], folder.Names);
	}

	[Fact]
	public void ALogStillHeld_RefusesTheOpen() {
		string path = folder.Write("a.ini", Before);
		Log(path, Before, After);
		using (new FileStream(IniLog.PathOf(path), FileMode.Open, FileAccess.Read, FileShare.None)) {
			IniCommitException refused = Assert.Throws<IniCommitException>(() => Open(path));
			Assert.Equal(IniCommitFailure.Running, refused.Failure);
			Assert.Equal(IniLog.PathOf(path), refused.Log);
		}
	}

	[Fact]
	public void ALogThatWillNotSettle_IsKept_AndTheFileRefusedUntilItDoes() {
		if (!OperatingSystem.IsWindows()) {
			return;
		}
		string path = folder.Write("a.ini", Before);
		Log(path, Before, After);
		//read, but not written: someone holds it open sharing reads only
		using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
			IniCommitException refused = Assert.Throws<IniCommitException>(() => Open(path));
			Assert.Equal(IniCommitFailure.Unsettled, refused.Failure);
			Assert.Equal(IniLog.PathOf(path), refused.Log);
		}
		Assert.Contains(".a.ini.tmp", folder.Names);
		using (IniTransaction file = Open(path)) {
			Assert.Equal(Value, file.Get("s", "x"));
		}
	}

	[Theory]
	[InlineData("prepared", Before, IniRecoveryKind.Unwhole)]
	[InlineData("logging", Before, IniRecoveryKind.Unwhole)]
	[InlineData("logged", After, IniRecoveryKind.Replayed)]
	[InlineData("writing", After, IniRecoveryKind.Replayed)]
	[InlineData("half", After, IniRecoveryKind.Kept)]
	[InlineData("written", After, IniRecoveryKind.Finished)]
	[InlineData("landing", After, IniRecoveryKind.Finished)]
	public void ACommitKilledAtEachStep_IsFinishedOrUndoneByTheNextOpen(string step, string text, IniRecoveryKind kind) {
		string path = folder.Write("a.ini", Before);
		Assert.NotEqual(0, Program.Run("commit", path, step, Value));
		using (IniTransaction file = Open(path)) {
			Assert.Equal(kind, file.Recovery?.Kind);
		}
		Assert.Equal(text, Folder.Read(path));
		Assert.DoesNotContain(".a.ini.tmp", folder.Names);
		if (kind == IniRecoveryKind.Kept) {
			//the half written file, kept
			Assert.NotEqual(Before, Folder.Read(folder[".a.ini.recovered"]));
		}
	}

	[Theory]
	[InlineData("prepared")]
	[InlineData("logging")]
	public void ANewFile_KilledBeforeItsLogIsWhole_IsNoFile(string step) {
		string path = folder["new.ini"];
		Assert.NotEqual(0, Program.Run("commit", path, step, "1"));
		using (IniTransaction file = Open(path)) {
			Assert.True(file.Document.IsNew);
		}
		Assert.Empty(folder.Names);
	}

	[Fact]
	public void ARecoveryKilledWhileKeepingTheBytes_IsFinishedByTheNext() {
		string path = folder.Write("a.ini", "[s]\nx=mine\n");
		Log(path, Before, After);
		Assert.NotEqual(0, Program.Run("open", path, "keeping"));
		Assert.Contains(".a.ini.recovered.tmp", folder.Names);
		using (IniTransaction file = Open(path)) {
			Assert.Equal(new IniRecovery(IniRecoveryKind.Kept, folder[".a.ini.recovered"]), file.Recovery);
		}
		Assert.Equal("[s]\nx=mine\n", Folder.Read(folder[".a.ini.recovered"]));
		Assert.Equal([".a.ini.recovered", "a.ini"], folder.Names);
	}

	[Fact]
	public void ARecoveryKilledAfterKeepingTheBytes_KeepsThemAgain_ASecondCopyRatherThanALostOne() {
		string path = folder.Write("a.ini", "[s]\nx=mine\n");
		Log(path, Before, After);
		Assert.NotEqual(0, Program.Run("open", path, "kept"));
		using (IniTransaction file = Open(path)) {
			Assert.Equal(folder[".a.ini.recovered-2"], file.Recovery?.Kept);
		}
		Assert.Equal(After, Folder.Read(path));
		Assert.Equal("[s]\nx=mine\n", Folder.Read(folder[".a.ini.recovered-2"]));
	}

	[Fact]
	public void ARecoveryKilledWhileWritingTheFile_IsFinishedByTheNext() {
		string path = folder.Write("a.ini", Before);
		Log(path, Before, After);
		Assert.NotEqual(0, Program.Run("open", path, "half"));
		using (IniTransaction file = Open(path)) {
			Assert.Equal(IniRecoveryKind.Kept, file.Recovery?.Kind);
		}
		Assert.Equal(After, Folder.Read(path));
	}

	[Fact]
	public void TheHelper_RunsToTheEnd_WhenNoStepKillsIt() {
		string path = folder.Write("a.ini", Before);
		Assert.Equal(0, Program.Run("commit", path, "never", Value));
		Assert.Equal(After, Folder.Read(path));
		Assert.Equal(["a.ini"], folder.Names);
	}
}
