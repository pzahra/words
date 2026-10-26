using System.Text;

namespace PatTech.Text.Ini;

/// <summary>
///     One file, loaded whole, edited in memory and committed once (SPEC.md, <i>A transaction</i>,
///     <i>The commit</i>): written ahead to a hidden log beside it, so a failure or a crash
///     partway is finished on the next open. Disposed without <see cref="Commit"/>, it leaves the
///     file as it never touched it.
/// </summary>
public sealed class IniTransaction : IDisposable {
	private FileStream? log;
	private FileStream? held;
	private byte[] bytes = [];
	private State state;

	private IniTransaction(string path, IniDocument document, IniRecovery? recovery) {
		Path = path;
		LogPath = IniLog.PathOf(path);
		Document = document;
		Recovery = recovery;
	}

	/// <summary>
	///     Opens <paramref name="path"/>, a link at its end followed to its file: held letting
	///     others read and none write while it is read, a log a commit left settled first. A file
	///     not there is a new one.
	/// </summary>
	/// <exception cref="IniCommitException">The file is held past half a second, another commit is running, or a log will not settle.</exception>
	public static IniTransaction Open(string path, IniDialect dialect) {
		ArgumentNullException.ThrowIfNull(path);
		ArgumentNullException.ThrowIfNull(dialect);
		string file = IniFiles.Resolve(path);
		string logPath = IniLog.PathOf(file);
		IniRecovery? recovery = null;
		for (int attempt = 1; ; attempt++) {
			//the file first, then its log: a commit ending pending while it looked is found once it holds the file
			using (FileStream? reading = IniFiles.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read, file, IniCommitFailure.Held)) {
				if (!File.Exists(logPath)) {
					IniDocument document = reading is null ? IniDocument.New(dialect, file) : IniDocument.Read(IniFiles.ReadAll(reading), dialect, file);
					return new IniTransaction(file, document, recovery);
				}
			}
			if (attempt == 5) {
				throw new IniCommitException(IniCommitFailure.Running, file, logPath);
			}
			recovery = Settle(file, logPath) ?? recovery;
		}
	}

	/// <summary>The file, a link followed.</summary>
	public string Path { get; }
	/// <summary>Where its log sits, while there is one: <c>.&lt;name&gt;.tmp</c> beside it.</summary>
	public string LogPath { get; }
	/// <summary>The file as loaded, and the edits held on it.</summary>
	public IniDocument Document { get; }
	/// <summary>What settling a log a commit left did, at the open or at <see cref="Prepare"/>; null for none.</summary>
	public IniRecovery? Recovery { get; private set; }

	/// <inheritdoc cref="IniDocument.Get"/>
	public string? Get(string section, string key) => Document.Get(section, key);

	/// <inheritdoc cref="IniDocument.Set"/>
	public void Set(string section, string key, string? value) => Document.Set(section, key, value);

	/// <inheritdoc cref="IniDocument.ReplaceSection"/>
	public void ReplaceSection(string section, IEnumerable<KeyValuePair<string, string>> pairs) => Document.ReplaceSection(section, pairs);

	/// <inheritdoc cref="IniDocument.RemoveSection"/>
	public void RemoveSection(string section) => Document.RemoveSection(section);

	/// <inheritdoc cref="IniDocument.Replace"/>
	public void Replace(string text, Encoding encoding, bool byteOrderMark = false) => Document.Replace(text, encoding, byteOrderMark);

	/// <summary>
	///     Makes the new bytes, takes the log, and holds the file, checked still what was read,
	///     until <see cref="Commit"/> or <see cref="Dispose"/>: everything about the file refuses
	///     here. A caller that must know the file will take its write calls it first. Nothing to
	///     write takes nothing.
	/// </summary>
	/// <exception cref="IniEncodingException">The text will not encode.</exception>
	/// <exception cref="IniCommitException">The file changed, is held or denied, another commit is running, or the log will not be made.</exception>
	public void Prepare() {
		Usable();
		if (state == State.Prepared) {
			return;
		}
		if (Document.Batching) {
			throw new InvalidOperationException("A batch is running: its edits are checked, and its bytes made, once it ends.");
		}
		byte[] made = Document.ToBytes();
		Document.Frozen = true;
		if (!Document.IsEdited || (!Document.IsNew && made.AsSpan().SequenceEqual(Document.Original.Span))) {
			state = State.Prepared;
			return;
		}
		try {
			log = TakeLog();
			held = IniFiles.Open(Path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, Path, IniCommitFailure.Held);
			bool unchanged = held is null
				? Document.IsNew
				: !Document.IsNew && IniFiles.ReadAll(held).AsSpan().SequenceEqual(Document.Original.Span);
			if (!unchanged) {
				throw new IniCommitException(IniCommitFailure.Changed, Path);
			}
		}
		catch {
			Release(deleteLog: true);
			Document.Frozen = false;
			throw;
		}
		bytes = made;
		state = State.Prepared;
		IniFiles.At("prepared");
	}

	/// <summary>
	///     Commits the edits: prepared if they are not, written ahead to the log, then the file
	///     written in place, let go, and the log deleted.
	/// </summary>
	/// <returns>Landed, noting a log that would not delete.</returns>
	/// <exception cref="IniEncodingException">Refused: the text will not encode.</exception>
	/// <exception cref="IniCommitException">Refused: nothing written, the file as it was, the log gone.</exception>
	/// <exception cref="IniPendingException">The log is whole and the commit stands, but the file's write failed: the next open finishes it.</exception>
	public IniLanded Commit() {
		Prepare();
		if (log is null) {
			state = State.Done;
			return new IniLanded(null);
		}
		try {
			IniLog.Write(log, Document.IsNew ? null : IniFiles.Hash(Document.Original.Span), bytes);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			//undone while it is still held: emptied, so it never reads whole, and deleted; one
			//that will do neither and reads whole stands, as a commit pending
			bool emptied = EmptyLog();
			bool deleted = DeleteLog();
			bool stands = !emptied && !deleted && LogIsWhole();
			Release(deleteLog: false);
			state = State.Done;
			if (stands) {
				throw new IniPendingException(Path, LogPath, e);
			}
			throw new IniCommitException(IniCommitFailure.LogWrite, Path, deleted ? null : LogPath, e);
		}
		state = State.Logged;
		IniFiles.At("logged");
		//a write that fails, or breaks off, is tried once more
		for (int attempt = 1; ; attempt++) {
			try {
				IniFiles.At("writing");
				held ??= new FileStream(Path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
				IniFiles.WriteOver(held, bytes);
				break;
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
				if (attempt == 2) {
					Release(deleteLog: false);
					state = State.Done;
					throw new IniPendingException(Path, LogPath, e);
				}
			}
		}
		IniFiles.At("written");
		IniFiles.At("landing");
		bool gone = DeleteLog();
		Release(deleteLog: false);
		IniFiles.At("landed");
		state = State.Done;
		return new IniLanded(gone ? null : LogPath);
	}

	/// <summary>Ends the transaction: one prepared lets the file go and deletes its log, nothing written.</summary>
	public void Dispose() {
		Release(deleteLog: state == State.Prepared);
		state = State.Done;
	}

	private void Usable() {
		if (state == State.Done) {
			throw new InvalidOperationException("The transaction has ended.");
		}
	}

	//the log deleted first, while it is still held, so it is never another commit's log of the
	//same name that goes; then both let go. It never throws: a handle that fails to close is
	//closed all the same, and the outcome already decided stands
	private void Release(bool deleteLog) {
		if (deleteLog && log is not null) {
			DeleteLog();
		}
		IniFiles.Close(ref log);
		IniFiles.Close(ref held);
	}

	//this commit's own log, deleted while it is held: its name goes, whoever makes the next
	private bool DeleteLog() => log is not null && IniFiles.TryDelete(LogPath, "deleting");

	private bool EmptyLog() {
		try {
			IniFiles.At("emptying");
			log!.SetLength(0);
			log.Flush(flushToDisk: true);
			return true;
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			return false;
		}
	}

	private bool LogIsWhole() {
		try {
			return IniLog.Read(log!) is not null;
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			return true; //not known to be broken: it may stand
		}
	}

	//the log made new and held, shared with none: one there is settled first, and one held is
	//another commit running, waited for a moment, then refused
	private FileStream TakeLog() {
		for (int attempt = 1; ; attempt++) {
			if (File.Exists(LogPath)) {
				if (attempt == 5) {
					throw new IniCommitException(IniCommitFailure.Running, Path, LogPath);
				}
				Recovery = Settle(Path, LogPath) ?? Recovery;
				continue;
			}
			try {
				var taken = new FileStream(LogPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Delete);
				IniFiles.Hide(LogPath);
				return taken;
			}
			catch (IOException e) when (IniFiles.IsExists(e) && attempt < 5) {
				//made since it was looked for: settled, or waited for, on the next turn
			}
			catch (UnauthorizedAccessException) when (attempt < 5) {
				//a log deleted while held, where its name outlives it until it is let go
				Thread.Sleep(50 * attempt);
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
				throw new IniCommitException(IniCommitFailure.LogWrite, Path, inner: e);
			}
		}
	}

	//a log a commit left, settled before the file is read: taken as a commit takes its own, and
	//deleted while it is held; null when it went before it could be taken
	private static IniRecovery? Settle(string file, string logPath) {
		using FileStream? log = IniFiles.Open(logPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete, file, IniCommitFailure.Running, logPath);
		if (log is null) {
			return null;
		}
		IniRecovery recovery = IniLog.Read(log) is { } whole
			? Replay(file, logPath, whole.Before, whole.Bytes)
			: new IniRecovery(IniRecoveryKind.Unwhole, null);
		IniFiles.TryDelete(logPath);
		return recovery;
	}

	//the file brought to the log's bytes, looked at reading first, so a file already written
	//needs no write: held as a commit holds it only when it is to be written
	private static IniRecovery Replay(string file, string logPath, byte[]? before, byte[] after) {
		byte[] written = IniFiles.Hash(after);
		using (FileStream? reading = IniFiles.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read, file, IniCommitFailure.Unsettled, logPath)) {
			if (reading is not null && IniFiles.Hash(IniFiles.ReadAll(reading)).AsSpan().SequenceEqual(written)) {
				return new IniRecovery(IniRecoveryKind.Finished, null);
			}
		}
		using FileStream? target = IniFiles.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, file, IniCommitFailure.Unsettled, logPath);
		byte[]? current = target is null ? null : IniFiles.ReadAll(target);
		byte[]? hash = current is null ? null : IniFiles.Hash(current);
		if (hash is not null && hash.AsSpan().SequenceEqual(written)) {
			return new IniRecovery(IniRecoveryKind.Finished, null);
		}
		bool untouched = hash is null ? before is null : before is not null && hash.AsSpan().SequenceEqual(before);
		string? kept = null;
		try {
			kept = untouched || current is null ? null : Keep(file, current);
			IniFiles.At("settling");
			FileStream stream = target ?? new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
			using (target is null ? stream : null) {
				IniFiles.WriteOver(stream, after);
			}
			return new IniRecovery(untouched ? IniRecoveryKind.Replayed : IniRecoveryKind.Kept, kept);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			throw new IniCommitException(IniCommitFailure.Unsettled, file, logPath, e, kept);
		}
	}

	//the file's bytes kept before a log writes over them: whole in a temporary first, a crash's
	//leftover going, then renamed to the first name free, never over another
	private static string Keep(string file, byte[] current) {
		string temporary = IniLog.RecoveredOf(file, 1) + ".tmp";
		File.Delete(temporary);
		using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
			stream.Write(current, 0, current.Length / 2);
			stream.Flush();
			IniFiles.At("keeping");
			stream.Write(current, current.Length / 2, current.Length - current.Length / 2);
			stream.Flush(flushToDisk: true);
		}
		for (int count = 1; ; count++) {
			string kept = IniLog.RecoveredOf(file, count);
			try {
				File.Move(temporary, kept, overwrite: false);
				IniFiles.At("kept");
				return kept;
			}
			catch (IOException) when (File.Exists(kept)) {
				//never written over: the next name
			}
		}
	}

	private enum State {
		Open,
		Prepared,
		Logged,
		Done,
	}
}
