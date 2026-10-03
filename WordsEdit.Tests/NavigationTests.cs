using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Input;
using WordsEdit.Utils;
using WordsEdit.ViewModels;
using Xunit;

namespace WordsEdit.Tests;

/// <summary>
///     Navigation (SPEC: Navigation): every change of the selection is a move in a
///     history that Back and Forward step along; a hand-made selection of a
///     neighbour is a step, not a push; a gone node's entry is dropped when reached;
///     Reset empties it; and where Back or Forward arrives shows through the filters
///     until the selection moves on.
/// </summary>
public class NavigationTests {
	private const string Ini = "value-en=English\n\n[a]\nvalue=A\n\n[b]\nvalue=B\n\n[c]\nvalue=C\n[.deep]\nvalue=Deep\n";

	private static MainWindowViewModel LoadedVm() {
		var vm = new MainWindowViewModel(new FakeDialogs());
		vm.LoadFile(new StringReader(Ini), "N");
		return vm;
	}

	private static KeyNode Node(MainWindowViewModel vm, string label) => MainWindowViewModelTests.Node(vm, $"N.{label}");

	private static void Visit(MainWindowViewModel vm, params string[] labels) {
		foreach (string label in labels) {
			vm.Tree.SelectedKeyNode = Node(vm, label);
		}
	}

	private static void Back(MainWindowViewModel vm) {
		Assert.True(vm.Tree.BackCommand.CanExecute(null));
		vm.Tree.BackCommand.Execute(null);
	}

	private static void Forward(MainWindowViewModel vm) {
		Assert.True(vm.Tree.ForwardCommand.CanExecute(null));
		vm.Tree.ForwardCommand.Execute(null);
	}

	[Fact]
	public void BackLandsOnTheFirst_AndForwardOnTheSecond() {
		var vm = LoadedVm();
		Assert.False(vm.Tree.BackCommand.CanExecute(null));
		Visit(vm, "a", "b");
		Assert.False(vm.Tree.ForwardCommand.CanExecute(null));

		Back(vm);
		Assert.Same(Node(vm, "a"), vm.Tree.SelectedKeyNode);
		Assert.True(Node(vm, "a").IsSelected);
		Assert.False(Node(vm, "b").IsSelected);
		Assert.False(vm.Tree.BackCommand.CanExecute(null));

		Forward(vm);
		Assert.Same(Node(vm, "b"), vm.Tree.SelectedKeyNode);
		Assert.False(vm.Tree.ForwardCommand.CanExecute(null));
	}

	[Fact]
	public void SelectingANeighbourByHand_IsAStep_AndKeepsWhatLiesAhead() {
		var vm = LoadedVm();
		Visit(vm, "a", "b", "c");
		Back(vm); //on b

		Visit(vm, "a"); //what Back points to: a Back
		Assert.Equal(["N.a", "N.b", "N.c"], vm.Tree.History.Entries);
		Visit(vm, "b"); //what Forward points to: a Forward
		Forward(vm);
		Assert.Same(Node(vm, "c"), vm.Tree.SelectedKeyNode);

		//walking two nodes by hand piles nothing up
		Visit(vm, "b", "c", "b", "c");
		Assert.Equal(["N.a", "N.b", "N.c"], vm.Tree.History.Entries);
		//and the same node twice is one entry
		Visit(vm, "c");
		Assert.Equal(3, vm.Tree.History.Entries.Count);
	}

	[Fact]
	public void ASelectionMatchingNeitherNeighbour_DropsTheForwardRun() {
		var vm = LoadedVm();
		Visit(vm, "a", "b", "c");
		Back(vm);
		Back(vm); //on a, b and c ahead

		Visit(vm, "c.deep");
		Assert.Equal(["N.a", "N.c.deep"], vm.Tree.History.Entries);
		Assert.False(vm.Tree.ForwardCommand.CanExecute(null));
	}

	[Fact]
	public void ARemovedNodesEntry_IsDroppedWhenReached() {
		var vm = LoadedVm();
		Visit(vm, "a", "c", "b");
		vm.RemoveNodeCommand.Execute(null); //b goes; its file is selected

		Back(vm); //past b, which no longer resolves
		Assert.Same(Node(vm, "c"), vm.Tree.SelectedKeyNode);
		Assert.DoesNotContain("N.b", vm.Tree.History.Entries);
		Back(vm);
		Assert.Same(Node(vm, "a"), vm.Tree.SelectedKeyNode);
	}

	[Fact]
	public void ResetEmptiesIt() {
		var vm = LoadedVm();
		Visit(vm, "a", "b");

		vm.ResetCore();

		Assert.Empty(vm.Tree.History.Entries);
		Assert.False(vm.Tree.BackCommand.CanExecute(null));
	}

	[Fact]
	public void BackOntoAFilteredOutNode_ShowsIt_AndMovingOnHidesItAgain() {
		var vm = LoadedVm();
		Visit(vm, "a", "b");
		vm.Tree.SearchFilterText = "deep"; //b hides, and the selection moves up to the file
		Assert.Same(vm.Tree.KeyNodes[0], vm.Tree.SelectedKeyNode);
		int hidden = vm.Tree.HiddenCount;

		Back(vm);
		KeyNode b = Node(vm, "b");
		Assert.Same(b, vm.Tree.SelectedKeyNode); //not moved up again
		Assert.True(b.IsVisible);
		Assert.Equal(hidden - 1, vm.Tree.HiddenCount);
		Assert.False(Node(vm, "a").IsVisible); //the rest still filtered

		Visit(vm, "c.deep");
		Assert.False(b.IsVisible);
		Assert.Equal(hidden, vm.Tree.HiddenCount);
	}

	[Fact]
	public void ArrivingOpensThePathToTheNode() {
		var vm = LoadedVm();
		Visit(vm, "c.deep", "a");
		Node(vm, "c").IsExpanded = false;

		Back(vm);

		Assert.True(Node(vm, "c").IsExpanded);
		Assert.True(vm.Tree.KeyNodes[0].IsExpanded);
	}

	[Fact]
	public void TheHistoryIsBounded_AndAGoneEntryTakesItsTwinWithIt() {
		var history = new SelectionHistory(capacity: 3);
		foreach (string label in new[] { "a", "b", "c", "d" }) {
			history.Visit(label);
		}
		Assert.Equal(["b", "c", "d"], history.Entries);

		//a, x, y, a with x and y gone: stepping back drops them, and the a before them is the same node
		history = new SelectionHistory();
		foreach (string label in new[] { "a", "x", "y", "a" }) {
			history.Visit(label);
		}
		Assert.Equal(["a", "x", "y", "a"], history.Entries);
		Assert.Null(history.Back(label => label is not ("x" or "y")));
		Assert.Equal(["a"], history.Entries);
		Assert.False(history.CanGoBack);
	}

	[Fact]
	public void TheMouseButtonsRunTheirCommands() {
		//the gesture the window binds Back and Forward to, through WPF's own input bindings
		RunSta(() => {
			int back = 0;
			var target = new Border();
			target.InputBindings.Add(new InputBinding(new DelegateCommand(() => back++), new MouseButtonGesture(MouseButton.XButton1)));

			target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.XButton1) { RoutedEvent = Mouse.MouseDownEvent });
			target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.XButton2) { RoutedEvent = Mouse.MouseDownEvent });
			target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.XButton1) { RoutedEvent = Mouse.MouseUpEvent });

			Assert.Equal(1, back);
		});
	}

	/// <summary>WPF elements insist on an STA thread; xunit runs MTA.</summary>
	internal static void RunSta(Action action) {
		ExceptionDispatchInfo? error = null;
		var thread = new Thread(() => {
			try {
				action();
			}
			catch (Exception e) {
				error = ExceptionDispatchInfo.Capture(e);
			}
		});
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		thread.Join();
		error?.Throw();
	}
}
