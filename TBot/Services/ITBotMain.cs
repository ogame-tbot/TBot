using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tbot.Includes;

namespace Tbot.Services {
	internal interface ITBotMain {
		Task<bool> Init(string settingPath,
			string alias,
			ITelegramMessenger telegramHandler);

		ValueTask DisposeAsync();
	}
}
