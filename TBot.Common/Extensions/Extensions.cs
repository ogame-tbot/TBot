using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace Tbot.Common.Extensions {
	public static class StringExtensions {
		public static string FirstCharToUpper(this string input) {
			return input switch {
				null => throw new ArgumentNullException(nameof(input)),
				"" => throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input)),
				_ => input.First().ToString().ToUpper() + input[1..]
			};
		}
	}
}
