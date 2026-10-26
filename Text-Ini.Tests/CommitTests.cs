using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace PatTech.Text.Ini.Tests;

/// <summary>
///     The commit (SPEC: The commit, Outcomes): written ahead and then in place, the
///     log gone; a file changed after it was read, held, or denied is refused at Prepare with no
///     log left; a second commit waits for the first's log; a read waits out a commit's write; a
///     log that will not write is refused and a file that will not write is pending; a log that
///     will not delete still lands; a hard link, a hidden file, a Unix mode and a symbolic link
///     are kept.
/// </summary>
[Collection("files")]
public sealed class CommitTests : IDisposable {
	private readonly Folder folder = new();

	public void Dispose() => folder.Dispose();

	private IniTransaction Open(string path) => IniTransaction.Open(path, Folder.Dialect);

	[Fact]
	public void ACommit_WritesTheFile_AndLeavesNoLog() {
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		using (IniTransaction file = Open(path)) {
			Assert.Null(file.Recovery);
			Assert.Equal(folder[".a.ini.tmp"], file.LogPath);
			file.Set("s", "x", "2");
			file.Set("s", "y", "3");
			Assert.Null(file.Commit().Log);
		}
		Assert.Equal("[s]\nx=2\ny=3\n", Folder.Read(path));
		Assert.Equal(["a.ini"], folder.Names);
	}

	[Fact]
	public void ATransactionDisposedUncommitted_OrPreparedAndDisposed_LeavesTheFileAsItWas() {
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		DateTime written = File.GetLastWriteTimeUtc(path);
		using (IniTransaction file = Open(path)) {
			file.Set("s", "x", "2");
		}
		using (IniTransaction file = Open(path)) {
			file.Set("s", "x", "2");
			file.Prepare();
			Assert.Contains(".a.ini.tmp", folder.Names);
		}
		Assert.Equal("[s]\nx=1\n", Folder.Read(path));
		Assert.Equal(written, File.GetLastWriteTimeUtc(path));
		Assert.Equal(["a.ini"], folder.Names);
	}

	[Fact]
	public void ANewFile_IsMadeByItsCommit_AndNotByAPrepareDisposed() {
		string path = folder["new.ini"];
		using (IniTransaction file = Open(path)) {
			Assert.True(file.Document.IsNew);
			file.Set("s", "x", "1");
			file.Prepare();
			Assert.False(File.Exists(path));
		}
		Assert.Empty(folder.Names);
		using (IniTransaction file = Open(path)) {
			file.Set("s", "x", "1");
			file.Commit();
		}
		Assert.Equal("[s]\nx=1\n", Folder.Read(path));
	}

	[Fact]
	public void NothingToWrite_TouchesNothing() {
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		DateTime written = File.GetLastWriteTimeUtc(path);
		using (IniTransaction file = Open(path)) {
			file.Set("s", "x", "2");
			file.Set("s", "x", "1");
			Assert.Null(file.Commit().Log);
		}
		Assert.Equal(written, File.GetLastWriteTimeUtc(path));
		using (IniTransaction none = Open(folder["none.ini"])) {
			none.Commit();
		}
		Assert.Equal(["a.ini"], folder.Names);
	}

	[Fact]
	public void AFileChangedAfterItWasRead_IsRefused_NothingWrittenAndNoLogLeft() {
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		using IniTransaction file = Open(path);
		file.Set("s", "x", "2");
		folder.Write("a.ini", "[s]\nx=9\n");
		IniCommitException refused = Assert.Throws<IniCommitException>(file.Commit);
		Assert.Equal(IniCommitFailure.Changed, refused.Failure);
		Assert.Equal(path, refused.Path);
		Assert.Equal("[s]\nx=9\n", Folder.Read(path));
		Assert.Equal(["a.ini"], folder.Names);

		//one made since a new one was read is as changed
		using IniTransaction made = Open(folder["new.ini"]);
		made.Set("s", "x", "1");
		folder.Write("new.ini", "mine");
		Assert.Equal(IniCommitFailure.Changed, Assert.Throws<IniCommitException>(made.Commit).Failure);
		Assert.Equal("mine", Folder.Read(folder["new.ini"]));
	}

	[Fact]
	public void ASecondCommit_WaitsForTheFirstsLog_ThenIsRefused() {
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		using IniTransaction first = Open(path);
		using IniTransaction second = Open(path);
		first.Set("s", "x", "2");
		second.Set("s", "x", "3");
		first.Prepare();
		IniCommitException refused = Assert.Throws<IniCommitException>(second.Commit);
		Assert.Equal(IniCommitFailure.Running, refused.Failure);
		Assert.Equal(first.LogPath, refused.Log);
		first.Commit();
		Assert.Equal("[s]\nx=2\n", Folder.Read(path));
		Assert.Equal(["a.ini"], folder.Names);
	}

	[Fact]
	public void AReadThroughTheLibrary_WaitsOutACommitsWrite() {
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		using IniTransaction writer = Open(path);
		writer.Set("s", "x", "2");
		writer.Prepare();
		Task committed = Task.Run(async () => {
			await Task.Delay(150);
			writer.Commit();
		});
		using IniTransaction reader = Open(path);
		committed.Wait();
		Assert.Equal("2", reader.Get("s", "x"));
	}

	[Fact]
	public void AFileHeldAMoment_IsWrittenOnceItLetsGo_AndOneHeldForGood_IsRefusedAtPrepare() {
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		using (IniTransaction file = Open(path)) {
			file.Set("s", "x", "2");
			var holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
			Task.Run(async () => {
				await Task.Delay(120);
				holder.Dispose();
			});
			file.Commit();
		}
		Assert.Equal("[s]\nx=2\n", Folder.Read(path));

		using IniTransaction held = Open(path);
		held.Set("s", "x", "3");
		using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) {
			IniCommitException refused = Assert.Throws<IniCommitException>(held.Prepare);
			Assert.Equal(IniCommitFailure.Held, refused.Failure);
		}
		Assert.Equal(["a.ini"], folder.Names);
	}

	[Fact]
	public void AReadOnlyFile_IsRefusedAtPrepare() {
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		File.SetAttributes(path, FileAttributes.ReadOnly);
		using IniTransaction file = Open(path);
		file.Set("s", "x", "2");
		Assert.Equal(IniCommitFailure.Denied, Assert.Throws<IniCommitException>(file.Prepare).Failure);
		Assert.Equal(["a.ini"], folder.Names);
	}

	[Fact]
	public void TextThatWillNotEncode_IsRefusedAtPrepare_BeforeAByteLands() {
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		using IniTransaction file = IniTransaction.Open(path, IniDialect.Sections(new IniRules { Encodings = [Encoding.Latin1] }));
		file.Set("s", "x", "€");
		Assert.Equal(IniEncodingFailure.Unencodable, Assert.Throws<IniEncodingException>(file.Prepare).Failure);
		Assert.Equal(["a.ini"], folder.Names);
	}

	[Fact]
	public void APreparedTransaction_TakesNoMoreEdits() {
		using IniTransaction file = Open(folder.Write("a.ini", "[s]\nx=1\n"));
		file.Set("s", "x", "2");
		file.Prepare();
		Assert.Throws<InvalidOperationException>(() => file.Set("s", "x", "3"));
		file.Commit();
		Assert.Throws<InvalidOperationException>(file.Commit);
	}

	[Fact]
	public void ALogThatFailsToWrite_IsRefused_TheFileUntouched() {
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		using IniTransaction file = Open(path);
		file.Set("s", "x", "2");
		IniFiles.Step = step => {
			if (step == "logging") {
				throw new IOException("the disk is full");
			}
		};
		Assert.Equal(IniCommitFailure.LogWrite, Assert.Throws<IniCommitException>(file.Commit).Failure);
		Assert.Equal("[s]\nx=1\n", Folder.Read(path));
		Assert.Equal(["a.ini"], folder.Names);
	}

	[Fact]
	public void AWriteThatFailsAfterTheLogIsWhole_IsPending_AndFinishedByTheNextOpen() {
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		using (IniTransaction file = Open(path)) {
			file.Set("s", "x", "2");
			IniFiles.Step = step => {
				if (step == "writing") {
					throw new IOException("the file went away");
				}
			};
			IniPendingException pending = Assert.Throws<IniPendingException>(file.Commit);
			Assert.Equal(file.LogPath, pending.Log);
			IniFiles.Step = null;
		}
		Assert.Equal("[s]\nx=1\n", Folder.Read(path));
		if (OperatingSystem.IsWindows()) {
			Assert.True(File.GetAttributes(folder[".a.ini.tmp"]).HasFlag(FileAttributes.Hidden));
		}
		using (IniTransaction next = Open(path)) {
			Assert.Equal(new IniRecovery(IniRecoveryKind.Replayed, null), next.Recovery);
			Assert.Equal("2", next.Get("s", "x"));
		}
		Assert.Equal(["a.ini"], folder.Names);
	}

	[Fact]
	public void ALogThatFailsToFlush_AndWillNeitherEmptyNorDelete_IsPending_AndTheNextOpenFinishesIt() {
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		using (IniTransaction file = Open(path)) {
			file.Set("s", "x", "2");
			//the trailer written, the flush failing, and the log not to be undone
			IniFiles.Step = step => {
				if (step is "flushing" or "emptying" or "deleting") {
					throw new IOException("the disk went away");
				}
			};
			Assert.Equal(file.LogPath, Assert.Throws<IniPendingException>(file.Commit).Log);
			IniFiles.Step = null;
		}
		using (IniTransaction next = Open(path)) {
			Assert.Equal(IniRecoveryKind.Replayed, next.Recovery?.Kind);
			Assert.Equal("2", next.Get("s", "x"));
		}

		//emptied though it will not delete: refused, and the log left never reads whole
		using (IniTransaction file = Open(path)) {
			file.Set("s", "x", "3");
			IniFiles.Step = step => {
				if (step is "flushing" or "deleting") {
					throw new IOException("the disk went away");
				}
			};
			IniCommitException refused = Assert.Throws<IniCommitException>(file.Commit);
			Assert.Equal((IniCommitFailure.LogWrite, file.LogPath), (refused.Failure, refused.Log));
			IniFiles.Step = null;
		}
		using (IniTransaction next = Open(path)) {
			Assert.Equal(IniRecoveryKind.Unwhole, next.Recovery?.Kind);
			Assert.Equal("2", next.Get("s", "x"));
		}
		Assert.Equal(["a.ini"], folder.Names);
	}

	[Fact]
	public void AHandleThatFailsToClose_LeavesTheOutcomeAsItWas_AndTheFileFree() {
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		using (IniTransaction file = Open(path)) {
			file.Set("s", "x", "2");
			IniFiles.Step = step => {
				if (step is "logging" or "closing") {
					throw new IOException("the disk is full");
				}
			};
			Assert.Equal(IniCommitFailure.LogWrite, Assert.Throws<IniCommitException>(file.Commit).Failure);
			IniFiles.Step = null;
		}
		using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
		}
		Assert.Equal(["a.ini"], folder.Names);
	}

	[Fact]
	public void ALogThatWillNotDelete_StillLands_AndTheNextOpenDeletesIt() {
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		using (IniTransaction file = Open(path)) {
			file.Set("s", "x", "2");
			IniFiles.Step = step => {
				if (step == "deleting") {
					throw new IOException("held by a scanner");
				}
			};
			Assert.Equal(file.LogPath, file.Commit().Log);
			IniFiles.Step = null;
		}
		Assert.Equal("[s]\nx=2\n", Folder.Read(path));
		using (IniTransaction next = Open(path)) {
			Assert.Equal(new IniRecovery(IniRecoveryKind.Finished, null), next.Recovery);
		}
		Assert.Equal(["a.ini"], folder.Names);
	}

	//a commit's log goes while the commit still holds it: one landing after another commit settled
	//the file and left its own log pending never deletes that one
	[Fact]
	public void ACommitLanding_NeverDeletesALaterCommitsLog() {
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		bool later = false;
		using (IniTransaction first = Open(path)) {
			first.Set("s", "x", "2");
			IniFiles.Step = step => {
				if (step == "landed" && !later) {
					later = true;
					using IniTransaction second = Open(path);
					second.Set("s", "x", "3");
					IniFiles.Step = inner => {
						if (inner == "writing") {
							throw new IOException("the file went away");
						}
					};
					Assert.Throws<IniPendingException>(second.Commit);
				}
			};
			first.Commit();
			IniFiles.Step = null;
		}
		Assert.True(later);
		Assert.Contains(".a.ini.tmp", folder.Names);
		using (IniTransaction next = Open(path)) {
			Assert.Equal(IniRecoveryKind.Replayed, next.Recovery?.Kind);
			Assert.Equal("3", next.Get("s", "x"));
		}
	}

	[Fact]
	public void AHiddenFile_StaysHidden_AndAHardLink_ReadsTheNewText() {
		if (!OperatingSystem.IsWindows()) {
			return;
		}
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		File.SetAttributes(path, FileAttributes.Hidden);
		Assert.True(CreateHardLink(folder["b.ini"], path, IntPtr.Zero));
		using (IniTransaction file = Open(path)) {
			file.Set("s", "x", "2");
			file.Commit();
		}
		Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Hidden));
		Assert.Equal("[s]\nx=2\n", Folder.Read(folder["b.ini"]));
	}

	[Fact]
	public void AUnixModesOrdinaryBits_AreKept() {
		if (OperatingSystem.IsWindows()) {
			return;
		}
		string path = folder.Write("a.ini", "[s]\nx=1\n");
		const UnixFileMode Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
		File.SetUnixFileMode(path, Mode);
		using (IniTransaction file = Open(path)) {
			file.Set("s", "x", "2");
			file.Commit();
		}
		Assert.Equal(Mode, File.GetUnixFileMode(path));
	}

	[Fact]
	public void ASymbolicLink_IsFollowedToItsFile_ItsLogBesideIt() {
		string target = folder.Write("target.ini", "[s]\nx=1\n");
		string link = folder["link.ini"];
		try {
			File.CreateSymbolicLink(link, target);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			//Windows makes links only with developer mode or as an administrator
			return;
		}
		using (IniTransaction file = Open(link)) {
			Assert.Equal(target, file.Path);
			Assert.Equal(folder[".target.ini.tmp"], file.LogPath);
			file.Set("s", "x", "2");
			file.Commit();
		}
		Assert.Equal("[s]\nx=2\n", Folder.Read(target));
		Assert.NotNull(new FileInfo(link).LinkTarget);
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}
