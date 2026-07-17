using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace WordsEdit {
	static class Extensions {
		/// <returns>
		/// <see langword="true"/> if <c>OK</c> or <c>Yes</c>; otherwise,
		/// <see langword="false"/>.
		/// </returns>
		public static bool IsAffirmative(this MessageBoxResult self) { 
			return self is MessageBoxResult.OK or MessageBoxResult.Yes; 
		}
		/// <returns>
		/// <see langword="true"/> if <c>No</c> or <c>Cancel</c>; otherwise,
		/// <see langword="false"/>.
		/// </returns>
	}
}
