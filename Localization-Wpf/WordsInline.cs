using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Threading;

namespace PatTech.Localization.Wpf {
	/// <summary>
	///     An inline that renders the Words for <see cref="Key"/> — markdown and all — inside a
	///     <see cref="System.Windows.Controls.TextBlock"/> or other flow content.
	/// </summary>
	/// <remarks>
	///     The resolved text may contain format placeholders, filled from <see cref="Params"/>:
	///     an array supplies positional <c>{0}</c>-style arguments, while any other single object
	///     supplies <c>{Name}</c>-style placeholders looked up by field or property name (see
	///     <see cref="Words.FormatByName(string, object?, object?[])"/>). Placeholders format
	///     with the thread's <see cref="CultureInfo.CurrentCulture"/> — the formatting culture
	///     <c>Digest</c> installed — rather than this element's <c>Language</c>, so the inline
	///     and a plain <c>Words.Format</c> agree. The rendered inlines are rebuilt whenever
	///     <see cref="Key"/> or <see cref="Params"/> changes.
	///     <para>
	///     Bind <see cref="Params"/> when there is an object or an array to hand to Format;
	///     populate the content with bindings instead and the XAML builds that array
	///     (<see cref="Args"/>): a stack of <c>Binding</c>s, or one <c>MultiBinding</c>,
	///     evaluated live so the inline follows its sources. A constant among them is a
	///     <c>Binding</c> with a <c>Source</c> and no path.
	///     </para>
	/// </remarks>
	[ContentProperty(nameof(Args))]
	public class WordsInline : Span, IKnowWords {
		/// <summary>Identifies the <see cref="Key"/> dependency property.</summary>
		public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(
			nameof(Key),
			typeof(string),
			typeof(WordsInline),
			new PropertyMetadata(propertyChangedCallback: KeyChanged));

		/// <summary>Identifies the <see cref="Params"/> dependency property.</summary>
		public static readonly DependencyProperty ParamsProperty = DependencyProperty.Register(
			nameof(Params),
			typeof(object),
			typeof(WordsInline),
			new PropertyMetadata(propertyChangedCallback: ParamsChanged));

		private static void KeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
			if (d is WordsInline w) {
				w.UpdateChild((string)e.NewValue, w.Params);
			}
		}
		private static void ParamsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
			if (d is WordsInline w) {
				w.UpdateChild(w.Key, e.NewValue);
			}
		}
		private void UpdateChild(string? key, object? @params) {
			if (string.IsNullOrEmpty(key)) {
				Inlines.Clear();
				return;
			}

			var text = Words.Known.FormatParams(key, @params, CultureInfo.CurrentCulture);

			// build fully, then swap: a formatting or parse failure above throws
			// before we touch Inlines, so the existing content stays put instead of
			// being blanked
			var built = new List<Inline>(MarkdownParser.Default.ToInlines(text));
			Inlines.Clear();
			Inlines.AddRange(built);
		}

		private bool resolvePending;

		public WordsInline() {
			// children arrive one Add at a time, XAML included, and a partial list has the wrong
			// number of positional arguments; resolve once, after the current work completes
			Args.CollectionChanged += (_, _) => {
				if (!resolvePending) {
					resolvePending = true;
					Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => { resolvePending = false; ResolveArgs(); }));
				}
			};
			// live mode: a swap of the dictionary renders the key again; off, a no-op
			Words.Watch(this);
		}

		/// <summary>
		///     The dictionary was swapped (<see cref="Words.Live"/>): renders <see cref="Key"/>
		///     again with the same <see cref="Params"/>, so the inline follows the language.
		/// </summary>
		public void Refresh() => UpdateChild(Key, Params);

		/// <summary>
		///     The key of the Words to render. A null or empty key renders nothing;
		///     an unknown key renders as <c>#key#</c>.
		/// </summary>
		[WordsKey]
		public string Key {
			get => (string)GetValue(KeyProperty);
			set => SetValue(KeyProperty, value);
		}

		/// <summary>
		///     Optional arguments for the resolved text's format placeholders. An array fills
		///     positional <c>{0}</c> placeholders; any other object fills <c>{Name}</c>
		///     placeholders from its public fields and properties. Bind it when there is an
		///     object or an array to hand to Format; to have the XAML build the array from
		///     bindings instead, populate the content — <see cref="Args"/> drives this property
		///     while it has any.
		/// </summary>
		public object? Params {
			get => GetValue(ParamsProperty);
			set => SetValue(ParamsProperty, value);
		}

		/// <summary>
		///     The arguments as child elements — the content property. WPF admits a
		///     <c>Binding</c> as a child only of a <c>Collection&lt;BindingBase&gt;</c>, so the
		///     children are bindings: a stack of <c>Binding</c>s, or one <c>MultiBinding</c>; a
		///     constant is a <c>Binding</c> with a <c>Source</c> and no path. One child sets
		///     <see cref="Params"/> as it is, so a bound array or a named object works as it
		///     would set directly, and a bare <c>MultiBinding</c> with no converter gets an
		///     <see cref="ArrayMultiConverter"/>; more than one become the positional array on
		///     an internal <c>MultiBinding</c> — which is why a <c>MultiBinding</c> child must
		///     stand alone, WPF being unable to nest one in another. While there are children
		///     they drive <see cref="Params"/>, resolved once the current operation completes,
		///     so a list built one child at a time is read whole, never half.
		/// </summary>
		public ObservableCollection<BindingBase> Args { get; } = new();

		// turns the children into Params: a lone binding passes through, several combine on one
		// MultiBinding so a change to any source rebuilds
		private void ResolveArgs() {
			BindingOperations.ClearBinding(this, ParamsProperty);
			var items = Args;
			if (items.Count == 0) {
				return;
			}
			if (items.Count == 1) {
				if (items[0] is MultiBinding multi) {
					multi.Converter ??= new ArrayMultiConverter();
				}
				BindingOperations.SetBinding(this, ParamsProperty, items[0]);
				return;
			}
			var combined = new MultiBinding { Converter = new ArrayMultiConverter() };
			foreach (var item in items) {
				combined.Bindings.Add(item);
			}
			BindingOperations.SetBinding(this, ParamsProperty, combined);
		}
	}
}
