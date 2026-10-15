global using static WordsEdit.Tests.TestTree;
using PatTech.Localization.Authoring;
using WordsEdit.ViewModels;

namespace WordsEdit.Tests;

/// <summary>
///     The tests name a node, and a key, by its file and its path there, as the editor does
///     (SPEC: Keys without the file's name): a file by its name, a root at an empty path.
/// </summary>
internal static class TestTree {
	/// <summary>The loaded file named <paramref name="name"/>.</summary>
	public static WordsFile FileNamed(MainWindowViewModel vm, string name) => vm.Session.Files.Single(file => file.Name == name);

	/// <summary>The node at <paramref name="path"/> in <paramref name="file"/>'s tree; its root at an empty one.</summary>
	public static KeyNode Node(MainWindowViewModel vm, string file, string path)
		=> vm.Tree.NodeAt(FileNamed(vm, file), path) ?? throw new InvalidOperationException($"no node at '{path}' in {file}");

	/// <summary>The key named <paramref name="name"/> in <paramref name="file"/>.</summary>
	public static WordsKey Key(MainWindowViewModel vm, string file, string name) => FileNamed(vm, file).Keys[name];
}
