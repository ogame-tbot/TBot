using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Includes;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure;

namespace Tbot.Services {
	public interface ITBotMain {
		Task<bool> Init(string settingPath,
			string alias,
			ITelegramMessenger telegramHandler);

		ValueTask DisposeAsync();

		dynamic InstanceSettings { get; }
		string InstanceAlias { get; }
		UserData UserData { get; set; } // Must be modifiable from outside so ITBotHelper can access it
		TelegramUserData TelegramUserData { get; }
		IOgameService OgamedInstance { get; }
		ICalculationService HelperService { get; }
		IFleetScheduler FleetScheduler { get; }
		DateTime NextWakeUpTime { get; set;}
		void log(LogLevel logLevel, LogSender sender, string format);
		Task SendTelegramMessage(string fmt);


		Task SleepNow(DateTime WakeUpTime);
	}
}
