using System;

namespace Sample_Ava.ViewModels {
	public partial class MainWindowViewModel : ViewModelBase {
		private double unread = 3;
		public double Unread {
			get => unread;
			set {
				if (ChangeProperty(ref unread, value)) AffectProperty(nameof(UnreadParams));
			}
		}
		/// <summary>Positional arguments for the `demo.params-positional` Words.</summary>
		public object[] UnreadParams => [(int)unread];

		public record ProfileInfo(string Name, DateTime Since);
		/// <summary>Named arguments for the `demo.params-named` Words, read by property name.</summary>
		public ProfileInfo Profile { get; } = new("Ada Lovelace", new DateTime(2025, 12, 10));

		private string playground = "Try your own: **bold**, *italic*, H~2~O, :sparkles: and [links](https://github.com \"with tooltips\")";
		public string Playground {
			get => playground;
			set => ChangeProperty(ref playground, value);
		}

		private Uri? lastCommand;
		private int commandCount;
		/// <summary>Positional arguments for the `demo.appcmd-report` Words.</summary>
		public object[] AppCommandParams => [commandCount, lastCommand?.ToString() ?? "—"];
		/// <summary>Called by the global hyperlink handler when an `appcmd:` link is clicked.</summary>
		public void TakeAppCommand(Uri uri) {
			lastCommand = uri;
			++commandCount;
			AffectProperty(nameof(AppCommandParams));
		}
	}
}
