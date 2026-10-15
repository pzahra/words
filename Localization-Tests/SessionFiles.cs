using PatTech.Localization.Authoring;

namespace PatTech.Localization.Tests;

/// <summary>The tests name a key by its file and its own name, as the session does (the editor's SPEC: Keys without the file's name).</summary>
internal static class SessionFiles {
	/// <summary>The loaded file named <paramref name="name"/>, the one of that name.</summary>
	public static WordsFile FileNamed(this WordsSession session, string name) => session.Files.Single(file => file.Name == name);
}
