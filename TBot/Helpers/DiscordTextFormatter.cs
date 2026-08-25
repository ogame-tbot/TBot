using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Tbot.Helpers {
	internal static class DiscordTextFormatter {
		public static string ToDiscordMarkdown(string message) {
			if (string.IsNullOrEmpty(message)) {
				return message;
			}
			var s = message
				.Replace("<code>", "`", StringComparison.OrdinalIgnoreCase)
				.Replace("</code>", "`", StringComparison.OrdinalIgnoreCase)
				.Replace("<b>", "**", StringComparison.OrdinalIgnoreCase)
				.Replace("</b>", "**", StringComparison.OrdinalIgnoreCase)
				.Replace("<i>", "*", StringComparison.OrdinalIgnoreCase)
				.Replace("</i>", "*", StringComparison.OrdinalIgnoreCase)
				.Replace("<pre>", "```\n", StringComparison.OrdinalIgnoreCase)
				.Replace("</pre>", "\n```", StringComparison.OrdinalIgnoreCase);
			s = Regex.Replace(s, "<[^>]+>", string.Empty);
			s = s.Replace("@everyone", "@\u200beveryone", StringComparison.OrdinalIgnoreCase)
				.Replace("@here", "@\u200bhere", StringComparison.OrdinalIgnoreCase);
			return s;
		}

		public static IEnumerable<string> Chunk(string message, int maxLen = 1900) {
			if (string.IsNullOrEmpty(message)) {
				yield break;
			}
			if (message.Length <= maxLen) {
				yield return message;
				yield break;
			}
			int offset = 0;
			while (offset < message.Length) {
				int len = Math.Min(maxLen, message.Length - offset);
				if (offset + len < message.Length) {
					int split = message.LastIndexOf('\n', offset + len - 1, len);
					if (split > offset) {
						len = split - offset + 1;
					}
				}
				yield return message.Substring(offset, len);
				offset += len;
			}
		}
	}
}
