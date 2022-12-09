using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Common.Logging.TextFormatters {
	public static class EmojiFormatter {

		private static readonly Dictionary<string, string> Emojis = new Dictionary<string, string>() {
			{ "Information", "ℹ" },
			{ "Warning", "⚠" },
			{ "Error", "🚫" },
			{ "Debug", "👉" },
			{ "Main", "🏠" },
			{ "Tbot", "🤖" },
			{ "OGameD", "🔌" },
			{ "Defender", "🛡" },
			{ "Brain", "🧠" },
			{ "Expeditions", "🚀" },
			{ "Harvest", "🌱" },
			{ "FleetScheduler", "⏱" },
			{ "SleepMode", "🛏" },
			{ "Colonize", "🛬" },
			{ "AutoFarm", "⚔" },
			{ "Telegram", "📢" }
		};

		public static string GetEmoji(string text) {
			if (Emojis.ContainsKey(text))
				return Emojis[text];
			return text;
		}
	}
}
