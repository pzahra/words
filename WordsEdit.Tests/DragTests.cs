using GongSolutions.Wpf.DragDrop;
using PatTech.Localization.Authoring;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>A drop as the library would describe it, with only what the handlers read filled in.</summary>
internal sealed class FakeDropInfo(object? data, object? target, RelativeInsertPosition position) : IDropInfo {
	public object Data { get; set; } = data!;
	public IDragInfo DragInfo { get; set; } = null!;
	public Point DropPosition { get; set; }
	public Type? DropTargetAdorner { get; set; }
	public DragDropEffects Effects { get; set; }
	public int InsertIndex { get; set; }
	public int UnfilteredInsertIndex { get; set; }
	public IEnumerable TargetCollection { get; set; } = Array.Empty<object>();
	public object TargetItem { get; set; } = target!;
	public CollectionViewGroup TargetGroup { get; set; } = null!;
	public ScrollViewer TargetScrollViewer { get; set; } = null!;
	public ScrollingMode TargetScrollingMode { get; set; }
	public UIElement VisualTarget { get; set; } = null!;
	public UIElement VisualTargetItem { get; set; } = null!;
	public Orientation VisualTargetOrientation { get; set; }
	public FlowDirection VisualTargetFlowDirection { get; set; }
	public string DestinationText { get; set; } = "";
	public string EffectText { get; set; } = "";
	public RelativeInsertPosition InsertPosition { get; set; } = position;
	public DragDropKeyStates KeyStates { get; set; }
	public bool NotHandled { get; set; }
	public bool IsSameDragDropContextAsSource { get; set; }
	public EventType EventType { get; set; }
}

/// <summary>A drag about to start, from one item.</summary>
internal sealed class FakeDragInfo(object? sourceItem) : IDragInfo {
	public object Data { get; set; } = null!;
	public DataFormat DataFormat { get; set; } = null!;
	public object DataObject { get; set; } = null!;
	public DragDropKeyStates DragDropCopyKeyState { get; set; }
	public Func<DependencyObject, object, DragDropEffects, DragDropEffects> DragDropHandler { get; set; } = null!;
	public Point DragStartPosition { get; set; }
	public DragDropEffects Effects { get; set; }
	public MouseButton MouseButton { get; set; }
	public Point PositionInDraggedItem { get; set; }
	public IEnumerable SourceCollection { get; set; } = Array.Empty<object>();
	public CollectionViewGroup SourceGroup { get; set; } = null!;
	public int SourceIndex { get; set; }
	public object SourceItem { get; set; } = sourceItem!;
	public IEnumerable SourceItems { get; set; } = Array.Empty<object>();
	public UIElement VisualSource { get; set; } = null!;
	public FlowDirection VisualSourceFlowDirection { get; set; }
	public UIElement VisualSourceItem { get; set; } = null!;
}

/// <summary>
///     Drag and drop in the tree, headless: the handler is fed drops the way the
///     library would describe them. The document moves first and refuses what it
///     must; the tree follows only when the document did.
/// </summary>
public class DragTests {
	private static (MainWindowViewModel vm, FakeDialogs dialogs, KeyDrag drag) Load() {
		var dialogs = new FakeDialogs();
		var vm = new MainWindowViewModel(dialogs);
		vm.LoadFile(MainWindowViewModelTests.GetExampleFileReader("WordsEdit.Tests.Resources.ExampleFile.ini"), "Example");
		vm.IsDirty = false;
		return (vm, dialogs, vm.KeyDrag);
	}

	private static KeyNode Node(MainWindowViewModel vm, string fullLabel) => MainWindowViewModelTests.Node(vm, fullLabel);

	private static string Save(MainWindowViewModel vm) {
		var writer = new StringWriter();
		vm.Session.Save(vm.Session.FileOf("Example")!, vm.Tree.KeyNodes[0], writer);
		return writer.ToString();
	}

	[Fact]
	public void Drop_ReparentsASubtreeAndItsKeys() {
		var (vm, dialogs, drag) = Load();
		KeyNode two = Node(vm, "Example.enum.two");
		KeyNode main = Node(vm, "Example.main");
		KeyNode enumNode = Node(vm, "Example.enum");

		drag.Drop(new FakeDropInfo(two, main, RelativeInsertPosition.TargetItemCenter));

		Assert.Same(main, two.Parent);
		Assert.Same(two, main.Children[^1]); //onto the center: last child
		Assert.DoesNotContain(two, enumNode.Children);
		Assert.Equal("Example.main.two", two.FullLabel);
		Assert.Equal(["Example.main.two.tooltip", "Example.main.two.desc"], two.Children.Select(child => child.FullLabel));
		Assert.True(vm.Session.Keys.ContainsKey("Example.main.two"));
		Assert.True(vm.Session.Keys.ContainsKey("Example.main.two.tooltip"));
		Assert.False(vm.Session.Keys.ContainsKey("Example.enum.two"));
		Assert.Equal("ZH:With Tooltip", vm.Session.Keys["Example.main.two.tooltip"].Entries["zh"].Value);
		Assert.True(vm.IsDirty);
		Assert.Empty(dialogs.Notices);
	}

	[Fact]
	public void Drop_BeforeOrAfterASiblingReordersWithoutTouchingTheKeys() {
		var (vm, _, drag) = Load();
		KeyNode enumNode = Node(vm, "Example.enum");
		KeyNode none = Node(vm, "Example.enum.none");
		KeyNode two = Node(vm, "Example.enum.two");
		int keys = vm.Session.Keys.Count;
		Assert.Equal([none, two], enumNode.Children);

		drag.Drop(new FakeDropInfo(none, two, RelativeInsertPosition.AfterTargetItem));
		Assert.Equal([two, none], enumNode.Children);

		drag.Drop(new FakeDropInfo(none, two, RelativeInsertPosition.BeforeTargetItem));
		Assert.Equal([none, two], enumNode.Children);

		Assert.Equal("Example.enum.none", none.FullLabel);
		Assert.Equal(keys, vm.Session.Keys.Count);
		Assert.Equal("No Selection", vm.Session.Keys["Example.enum.none"].DefaultValue);
		Assert.True(vm.IsDirty); //the order is written
	}

	[Fact]
	public void Drop_ReordersFilesAmongThemselvesOnly() {
		var (vm, _, drag) = Load();
		vm.LoadFile(new StringReader("value-en=English\n\n[b]\nvalue=B\n"), "Lib");
		KeyNode example = vm.Tree.KeyNodes[0];
		KeyNode lib = vm.Tree.KeyNodes[1];

		drag.Drop(new FakeDropInfo(lib, example, RelativeInsertPosition.BeforeTargetItem));
		Assert.Equal(["Lib", "Example"], vm.Tree.FileLabels); //lookup precedence, not document content

		drag.Drop(new FakeDropInfo(lib, example, RelativeInsertPosition.AfterTargetItem));
		Assert.Equal(["Example", "Lib"], vm.Tree.FileLabels);

		//a file lands nowhere but among files
		drag.Drop(new FakeDropInfo(lib, Node(vm, "Example.main"), RelativeInsertPosition.TargetItemCenter));
		Assert.Equal(["Example", "Lib"], vm.Tree.FileLabels);
		Assert.Null(lib.Parent);
		Assert.True(vm.Session.Keys.ContainsKey("Lib.b"));
	}

	[Fact]
	public void Drop_MovesACommentWithoutAskingTheDocument() {
		var (vm, _, drag) = Load();
		KeyNode main = Node(vm, "Example.main");
		var banner = Assert.IsType<CommentNode>(main.Children[0]);
		KeyNode singleLine = Node(vm, "Example.main.single-line");
		int keys = vm.Session.Keys.Count;

		drag.Drop(new FakeDropInfo(banner, singleLine, RelativeInsertPosition.AfterTargetItem));

		Assert.Same(banner, main.Children[^1]);
		Assert.Equal(keys, vm.Session.Keys.Count);
		Assert.True(vm.IsDirty);
		string saved = Save(vm);
		Assert.True(saved.IndexOf("[.single-line]", StringComparison.Ordinal) < saved.IndexOf("; a banner", StringComparison.Ordinal),
			"the comment is written where it now stands");
	}

	[Fact]
	public void Drop_OntoOwnDescendantChangesNothing() {
		var (vm, _, drag) = Load();
		KeyNode main = Node(vm, "Example.main");
		KeyNode title = Node(vm, "Example.main.title");
		KeyNode file = vm.Tree.KeyNodes[0];

		var over = new FakeDropInfo(main, title, RelativeInsertPosition.TargetItemCenter);
		drag.DragOver(over);
		Assert.Equal(typeof(DropTargetAdorner), over.DropTargetAdorner); //the plain adorner: not here

		drag.Drop(over);
		Assert.Same(file, main.Parent);
		Assert.Same(main, title.Parent);
		Assert.True(vm.Session.Keys.ContainsKey("Example.main.title"));
		Assert.False(vm.IsDirty);
	}

	[Fact]
	public void Drop_WithNothingUnderItChangesNothing() {
		var (vm, _, drag) = Load();
		KeyNode none = Node(vm, "Example.enum.none");
		KeyNode enumNode = Node(vm, "Example.enum");

		var over = new FakeDropInfo(none, null, RelativeInsertPosition.None);
		drag.DragOver(over);
		Assert.Equal(DragDropEffects.Move, over.Effects);
		Assert.Null(over.DropTargetAdorner);

		drag.Drop(over);
		Assert.Same(enumNode, none.Parent);
		Assert.Equal(0, enumNode.Children.IndexOf(none));
		Assert.False(vm.IsDirty);
	}

	[Fact]
	public void Drop_OntoASameNamedSiblingIsRefusedAndReported() {
		var (vm, dialogs, drag) = Load();
		KeyNode tooltip = Node(vm, "Example.enum.two.tooltip");
		KeyNode key = Node(vm, "Example.view.section-name.key"); //already has a tooltip
		KeyNode two = Node(vm, "Example.enum.two");

		drag.Drop(new FakeDropInfo(tooltip, key, RelativeInsertPosition.TargetItemCenter));

		Assert.Contains("tooltip", Assert.Single(dialogs.Notices));
		Assert.Same(two, tooltip.Parent);
		Assert.Equal("Example.enum.two.tooltip", tooltip.FullLabel);
		Assert.Equal("With Tooltip", vm.Session.Keys["Example.enum.two.tooltip"].DefaultValue);
		Assert.Equal("Base", vm.Session.Keys["Example.view.section-name.key.tooltip"].DefaultValue); //nothing overwritten
		Assert.False(vm.IsDirty);
	}

	[Fact]
	public void Drop_KeepsAConstantDirectlyUnderItsFile() {
		var (vm, _, drag) = Load();
		KeyNode constant = Node(vm, "Example.$rsi-unit");
		KeyNode main = Node(vm, "Example.main");
		KeyNode file = vm.Tree.KeyNodes[0];

		//dropped onto a group, it lands beside the group instead
		drag.Drop(new FakeDropInfo(constant, main, RelativeInsertPosition.TargetItemCenter));

		Assert.Same(file, constant.Parent);
		Assert.Equal(file.Children.IndexOf(main) - 1, file.Children.IndexOf(constant));
		Assert.True(constant.IsConstant);
		Assert.True(vm.Session.Keys.ContainsKey("Example.$rsi-unit"));
		Assert.Contains("[$rsi-unit]", Save(vm));
	}

	[Fact]
	public void DragOver_ShowsWhatADropWouldDo() {
		var (vm, _, drag) = Load();
		KeyNode none = Node(vm, "Example.enum.none");
		KeyNode two = Node(vm, "Example.enum.two");
		KeyNode banner = Node(vm, "Example.main").Children[0];
		KeyNode preamble = vm.Tree.KeyNodes[0].Children[0];

		var center = new FakeDropInfo(none, two, RelativeInsertPosition.TargetItemCenter);
		drag.DragOver(center);
		Assert.Equal(typeof(DropTargetHighlightAdorner), center.DropTargetAdorner); //becomes a child

		var after = new FakeDropInfo(none, two, RelativeInsertPosition.AfterTargetItem);
		drag.DragOver(after);
		Assert.Equal(typeof(DropTargetInsertionAdorner), after.DropTargetAdorner); //slots in beside

		var ontoComment = new FakeDropInfo(none, banner, RelativeInsertPosition.TargetItemCenter);
		drag.DragOver(ontoComment);
		Assert.Equal(typeof(DropTargetAdorner), ontoComment.DropTargetAdorner); //comments take no children

		var pinned = new FakeDropInfo(preamble, two, RelativeInsertPosition.AfterTargetItem);
		drag.DragOver(pinned);
		Assert.Equal(typeof(DropTargetAdorner), pinned.DropTargetAdorner); //the preamble does not move
		Assert.False(drag.CanStartDrag(new FakeDragInfo(preamble)));
		Assert.True(drag.CanStartDrag(new FakeDragInfo(banner)));
		Assert.True(drag.CanStartDrag(new FakeDragInfo(none)));
		Assert.False(drag.CanStartDrag(new FakeDragInfo(null)));

		var start = new FakeDragInfo(none);
		drag.StartDrag(start);
		Assert.Same(none, start.Data);
		Assert.Equal(DragDropEffects.Move, start.Effects);

		Assert.Throws<InvalidOperationException>(() => new KeyDrag().DragOver(center));
	}

	[Fact]
	public void LanguageDrop_ReordersTheTableForEveryFile() {
		var (vm, _, _) = Load();
		var manager = new LanguageManagerViewModel(vm);
		LanguageDrag drag = manager.LanguageDrag;
		LanguageEntry en = vm.Tree.KnownLanguages[0];
		LanguageEntry zh = vm.Tree.KnownLanguages.First(l => l.Code == "zh");
		Assert.True(drag.CanStartDrag(new FakeDragInfo(zh)));

		drag.Drop(new FakeDropInfo(zh, en, RelativeInsertPosition.BeforeTargetItem));

		Assert.Equal("zh", vm.Tree.KnownLanguages[0].Code);
		Assert.Equal("zh", vm.Session.Files[0].Languages[0]);
		Assert.True(vm.IsDirty);

		vm.IsDirty = false;
		drag.Drop(new FakeDropInfo(zh, zh, RelativeInsertPosition.AfterTargetItem)); //onto itself: nothing
		Assert.False(vm.IsDirty);
		Assert.Throws<InvalidOperationException>(() => { new LanguageDrag().Drop(new FakeDropInfo(zh, en, RelativeInsertPosition.BeforeTargetItem)); });
	}
}
