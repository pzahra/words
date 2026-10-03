namespace PatTech.Localization {
	/// <summary>
	/// Something that holds rendered Words and can read them again: the seam the live
	/// registry calls when <see cref="Words.Known"/> is swapped. <see cref="LazyWords"/>
	/// is the built-in implementer; a view model that wants to re-raise its own
	/// notifications on a swap implements it and calls <see cref="Words.Watch"/>.
	/// </summary>
	public interface IKnowWords {
		/// <summary>
		/// The dictionary changed: drop what was read from it and tell whoever is
		/// listening. Called on the thread the watcher registered on.
		/// </summary>
		void Refresh();
	}
}
