using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tbot.Services;

namespace Tbot.Models {
	sealed class TbotInstanceData : IEquatable<TbotInstanceData> {
		public TBotMain _botMain;
		public string _botSettingsPath;
		public string _alias;

		public TbotInstanceData(TBotMain botMain, string botSettingsPath, string alias) {
			_botMain = botMain;
			_botSettingsPath = botSettingsPath;
			_alias = alias;
		}

		public bool Equals(TbotInstanceData other) {
			// Equality must check of settings path is the same, since user may change Alias and settings internal data
			if (other == null)
				return false;

			return (_botSettingsPath == other._botSettingsPath);
		}
	}
}
