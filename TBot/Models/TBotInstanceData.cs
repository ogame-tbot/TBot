using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Tbot.Services;

namespace Tbot.Models {
	sealed class TbotInstanceData : IEquatable<TbotInstanceData> {
		public ITBotMain _botMain;
		public IServiceScope ServiceScope;
		public string _botSettingsPath;
		public string _alias;

		public TbotInstanceData(ITBotMain botMain, IServiceScope serviceScope, string botSettingsPath, string alias) {
			_botMain = botMain;
			_botSettingsPath = botSettingsPath;
			_alias = alias;
			ServiceScope = serviceScope;
		}

		public bool Equals(TbotInstanceData other) {
			// Equality must check of settings path is the same, since user may change Alias and settings internal data
			if (other == null)
				return false;

			return (_botSettingsPath == other._botSettingsPath);
		}

		public async Task Deinitialize() {
			await _botMain.DisposeAsync();
			ServiceScope.Dispose();
		}
	}
}
