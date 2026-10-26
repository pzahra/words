using System.Buffers.Binary;

namespace PatTech.Text.Ini;

/// <summary>
///     The log a commit is written ahead to, <c>.&lt;name&gt;.tmp</c> beside the file (SPEC.md,
///     <i>The commit</i>): the file's hash before, or none for a file not there, the new bytes
///     whole, and last a trailer with their length and hash. Whole, the commit stands.
/// </summary>
internal static class IniLog {
	private static ReadOnlySpan<byte> Head => "INI-LOG1"u8;
	private static ReadOnlySpan<byte> Tail => "INI-END1"u8;
	private const int HashLength = 32;
	//head, whether there was a file, and its hash
	private const int Before = 8 + 1 + HashLength;
	//length, hash and tail
	private const int Trailer = 8 + HashLength + 8;

	internal static string PathOf(string file) => Path.Combine(Path.GetDirectoryName(file) ?? "", $".{Path.GetFileName(file)}.tmp");

	internal static string RecoveredOf(string file, int count)
		=> Path.Combine(Path.GetDirectoryName(file) ?? "", $".{Path.GetFileName(file)}.recovered{(count == 1 ? "" : $"-{count}")}");

	//the log written whole and flushed to disk; before is the file's hash, null for no file
	internal static void Write(FileStream log, byte[]? before, byte[] bytes) {
		var head = new byte[Before];
		Head.CopyTo(head);
		if (before is not null) {
			head[8] = 1;
			before.CopyTo(head, 9);
		}
		var trailer = new byte[Trailer];
		BinaryPrimitives.WriteInt64LittleEndian(trailer, bytes.Length);
		IniFiles.Hash(bytes).CopyTo(trailer, 8);
		Tail.CopyTo(trailer.AsSpan(8 + HashLength));
		log.Position = 0;
		log.Write(head);
		log.Write(bytes);
		IniFiles.At("logging");
		log.Write(trailer);
		log.SetLength(log.Position);
		IniFiles.At("flushing");
		log.Flush(flushToDisk: true);
	}

	//the file's hash before, null for none, and the new bytes; null for a log never whole
	internal static (byte[]? Before, byte[] Bytes)? Read(FileStream log) {
		byte[] all = IniFiles.ReadAll(log);
		if (all.Length < Before + Trailer || !all.AsSpan(0, 8).SequenceEqual(Head) || !all.AsSpan(all.Length - 8).SequenceEqual(Tail)) {
			return null;
		}
		long length = BinaryPrimitives.ReadInt64LittleEndian(all.AsSpan(all.Length - Trailer));
		if (length != all.Length - Before - Trailer) {
			return null;
		}
		byte[] bytes = all[Before..(all.Length - Trailer)];
		if (!IniFiles.Hash(bytes).AsSpan().SequenceEqual(all.AsSpan(all.Length - Trailer + 8, HashLength))) {
			return null;
		}
		return (all[8] == 1 ? all[9..Before] : null, bytes);
	}
}
