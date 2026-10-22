using System.Windows.Input;
using WordsEdit.Utils;

namespace WordsEdit.ViewModels;

/// <summary>
///     A help bubble's topic (SPEC: Help bubbles, The topics): its id, its revision,
///     and its page, read by its literal key so the editor's words name every one.
///     Raise the revision in the commit that changes the page, and the bubble comes
///     back for everyone who closed the old one; a typo fix leaves it alone.
/// </summary>
public sealed record HelpTopic(string Id, int Revision, Func<string> Page);

/// <summary>The bubbles' catalogue, in the order they are offered when two wait at once.</summary>
public static class HelpTopics {
	public static HelpTopic Start { get; } = new("start", 1, () => Words.Known["help.start"]);
	public static HelpTopic Tree { get; } = new("tree", 1, () => Words.Known["help.tree"]);

	public static IReadOnlyList<HelpTopic> All { get; } = [Start, Tree];

	/// <summary>The topic named <paramref name="id"/>, or null when the catalogue has none.</summary>
	public static HelpTopic? Find(string id) => All.FirstOrDefault(topic => topic.Id == id);
}

/// <summary>What the bubbles remember between runs: whether tips are on, and the revision of each bubble closed.</summary>
public interface IHelpMemory {
	bool Tips { get; set; }
	/// <summary>The revision of <paramref name="id"/> last closed; 0 when it never was.</summary>
	int Seen(string id);
	void Remember(string id, int revision);
	/// <summary>Forgets every revision closed, so each bubble comes back.</summary>
	void Forget();
}

/// <summary>The bubbles' memory for one run alone: what a test, or an editor built without the config, keeps.</summary>
public sealed class HelpMemory : IHelpMemory {
	private readonly Dictionary<string, int> seen = [];
	public bool Tips { get; set; } = true;
	public int Seen(string id) => seen.GetValueOrDefault(id);
	public void Remember(string id, int revision) => seen[id] = revision;
	public void Forget() => seen.Clear();
}

/// <summary>The bubbles' memory in the editor's config: <c>tips</c> under <c>[editor]</c>, a line each under <c>[seen]</c>.</summary>
public sealed class ConfigHelpMemory : IHelpMemory {
	public bool Tips { get => EditorConfig.Tips; set => EditorConfig.Tips = value; }
	public int Seen(string id) => EditorConfig.Seen(id);
	public void Remember(string id, int revision) => EditorConfig.Remember(id, revision);
	public void Forget() => EditorConfig.ForgetSeen();
}

/// <summary>
///     The help bubbles (SPEC: Help bubbles): the owner of a moment says it has come
///     (<see cref="Arise"/>) or gone (<see cref="Pass"/>), the view says which
///     topics have their control on screen (<see cref="Anchor"/>), and a topic opens
///     when all four hold: its moment, its control, tips on, and a revision above
///     the one remembered. One opens at a time; a moment that arises while another
///     is open waits for the next time it arises. Got it remembers the revision; a
///     bubble whose moment passes first goes unremembered, and comes back.
/// </summary>
public sealed class HelpBubbles : ViewModelBase {
	private readonly IHelpMemory memory;
	//moments that have come and wait for their control, and the topics whose control is on screen
	private readonly HashSet<string> pending = [];
	private readonly HashSet<string> anchored = [];

	/// <summary>The topic showing, or null.</summary>
	public HelpTopic? Open { get; private set => _ = ChangeProperty(ref field, value) && AffectProperty(nameof(Page)); }
	/// <summary>The open topic's page, markdown; empty while none is open.</summary>
	public string Page => Open?.Page() ?? "";
	/// <summary>Tips on: off, no bubble shows, and the one open goes unremembered.</summary>
	public bool Tips {
		get => memory.Tips;
		set {
			if (value == memory.Tips) {
				return;
			}
			memory.Tips = value;
			if (!value) {
				pending.Clear();
				Open = null;
			}
			AffectProperty(nameof(Tips));
		}
	}

	public ICommand GotItCommand { get; }

	public HelpBubbles(IHelpMemory memory) {
		this.memory = memory;
		GotItCommand = new DelegateCommand(GotIt, () => Open is not null);
	}

	/// <summary>The moment of <paramref name="topic"/> has come: it opens once its control is on screen, unless another is open, tips are off, or it was closed at this revision.</summary>
	public void Arise(HelpTopic topic) {
		if (Open is not null || !Tips || memory.Seen(topic.Id) >= topic.Revision) {
			return;
		}
		pending.Add(topic.Id);
		Show();
	}

	/// <summary>The moment of <paramref name="topic"/> has gone: it no longer waits, and closes unremembered if open.</summary>
	public void Pass(HelpTopic topic) {
		pending.Remove(topic.Id);
		if (Open == topic) {
			Open = null;
			Show();
		}
	}

	/// <summary>The control a topic points at came on screen, or left it; leaving passes its moment.</summary>
	public void Anchor(string id, bool onScreen) {
		if (onScreen) {
			anchored.Add(id);
			Show();
		}
		else if (anchored.Remove(id) && HelpTopics.Find(id) is { } topic) {
			Pass(topic);
		}
	}

	/// <summary>Got it: the open bubble goes for good, its revision remembered.</summary>
	public void GotIt() {
		if (Open is not { } topic) {
			return;
		}
		memory.Remember(topic.Id, topic.Revision);
		pending.Remove(topic.Id);
		Open = null;
		Show();
	}

	/// <summary>Show the tips again: every revision forgotten, so each bubble comes back when its moment does.</summary>
	public void ShowAgain() => memory.Forget();

	//the first topic whose moment waits with its control on screen, when none is open
	private void Show() {
		if (Open is null) {
			Open = HelpTopics.All.FirstOrDefault(topic => pending.Contains(topic.Id) && anchored.Contains(topic.Id));
		}
	}
}
