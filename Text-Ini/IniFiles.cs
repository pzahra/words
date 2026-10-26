using System.Security.Cryptography;

namespace PatTech.Text.Ini;

//the file and its log on disk (SPEC.md, The commit)
internal static class IniFiles {
	//a test's hook at each step of a commit and a recovery, to kill the process there or fail the step
	[ThreadStatic]
	internal static Action<string>? Step;

	internal static void At(string step) => Step?.Invoke(step);

	//the file a path names, a symbolic link at its end followed to its file: a link in a folder
	//along the way finds the same folder, so the log beside it is the same one
	internal static string Resolve(string path) {
		string full = Path.GetFullPath(path);
		try {
			return File.ResolveLinkTarget(full, returnFinalTarget: true)?.FullName ?? full;
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			return full;
		}
	}

	//path opened as share says, tried again over half a second while someone else holds it, then
	//refused as held, naming file and any log in the way; null when it is not there to open
	internal static FileStream? Open(string path, FileMode mode, FileAccess access, FileShare share, string file, IniCommitFailure held, string? log = null) {
		for (int attempt = 1; ; attempt++) {
			try {
				return new FileStream(path, mode, access, share);
			}
			catch (Exception e) when (mode == FileMode.Open && e is FileNotFoundException or DirectoryNotFoundException) {
				return null;
			}
			catch (UnauthorizedAccessException e) {
				throw new IniCommitException(IniCommitFailure.Denied, file, log, e);
			}
			catch (IOException e) when (IsHeld(e)) {
				if (attempt == 5) {
					throw new IniCommitException(held, file, log, e);
				}
				Thread.Sleep(50 * attempt);
			}
		}
	}

	//another handle shares nothing with the one asked for
	private static bool IsHeld(IOException e)
		=> OperatingSystem.IsWindows() ? (e.HResult & 0xFFFF) is 32 or 33 : e.HResult is 11 or 35;

	//a file made since it was looked for
	internal static bool IsExists(IOException e)
		=> OperatingSystem.IsWindows() ? (e.HResult & 0xFFFF) is 80 or 183 : e.HResult == 17;

	internal static byte[] ReadAll(FileStream stream) {
		stream.Position = 0;
		var bytes = new MemoryStream();
		stream.CopyTo(bytes);
		return bytes.ToArray();
	}

	//written over in place, so a file keeps its links, its attributes and its mode: the bytes, cut
	//to their length, flushed to disk
	internal static void WriteOver(FileStream stream, byte[] bytes) {
		stream.Position = 0;
		int half = bytes.Length / 2;
		stream.Write(bytes, 0, half);
		stream.Flush();
		At("half");
		stream.Write(bytes, half, bytes.Length - half);
		stream.SetLength(bytes.Length);
		stream.Flush(flushToDisk: true);
	}

	internal static byte[] Hash(ReadOnlySpan<byte> bytes) => SHA256.HashData(bytes);

	//hidden on Windows, as Office keeps its own; elsewhere its dot hides it
	internal static void Hide(string path) {
		if (OperatingSystem.IsWindows()) {
			try {
				File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
				//shown is no failure
			}
		}
	}

	//step names a test's hook, to fail the delete
	internal static bool TryDelete(string path, string? step = null) {
		try {
			if (step is not null) {
				At(step);
			}
			File.Delete(path);
			return true;
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
			return false;
		}
	}

	//a handle let go, never throwing: one whose last flush fails is closed all the same
	internal static void Close(ref FileStream? stream) {
		try {
			stream?.Dispose();
			if (stream is not null) {
				At("closing");
			}
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
		}
		stream = null;
	}
}
