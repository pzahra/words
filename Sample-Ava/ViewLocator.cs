using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Sample_Shared.ViewModels;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Sample_Ava {
	/// <summary>
	/// Given a view model, returns the corresponding view if possible.
	/// </summary>
	[RequiresUnreferencedCode(
		"Default implementation of ViewLocator involves reflection which may be trimmed away.",
		Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
	public class ViewLocator : IDataTemplate {
		public Control? Build(object? param) {
			if (param is null)
				return null;

			//the view models are Sample-Shared's, the views this sample's: XPageViewModel shows as Views.XPageView
			var name = "Sample_Ava.Views." + param.GetType().Name.Replace("ViewModel", "View", StringComparison.Ordinal);
			var type = Type.GetType(name);

			if (type != null) {
				return (Control)Activator.CreateInstance(type)!;
			}

			return new TextBlock { Text = "Not Found: " + name };
		}

		public bool Match(object? data) {
			return data is ViewModelBase;
		}
	}
}
