using System.Text;

namespace PatTech.Text.Ini.Tests;

//a test's own folder of files, gone after it
public sealed class Folder : IDisposable {
	private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("text-ini-");

	public static IniDialect Dialect { get; } = IniDialect.Sections(new IniRules { LineBreak = "\n" });

	public string this[string name] => Path.Combine(directory.FullName, name);

	public string Write(string name, string text) {
		string path = this[name];
		File.WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
		return path;
	}

	public static string Read(string path) => Encoding.UTF8.GetString(File.ReadAllBytes(path));

	//every name in it, hidden ones too, in order
	public IReadOnlyList<string> Names => [.. directory.GetFiles().Select(file => file.Name).Order(StringComparer.Ordinal)];

	public void Dispose() {
		IniFiles.Step = null;
		foreach (FileInfo file in directory.GetFiles()) {
			file.Attributes = FileAttributes.Normal;
		}
		directory.Delete(recursive: true);
	}
}
