using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Includes;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Services {
	public interface ITBotMain {
		event EventHandler OnError;
		Task Init(string settingPath,
			string alias,
			ITelegramMessenger telegramHandler);

		ValueTask DisposeAsync();

		dynamic InstanceSettings { get; }
		string InstanceAlias { get; }
		UserData UserData { get; set; }
		TelegramUserData TelegramUserData { get; }
		long SleepDuration { get; set; }
		DateTime NextWakeUpTime { get; set;}
		void log(LogLevel logLevel, LogSender sender, string format);
		Task InitializeFeature(Feature feat);
		Task StopFeature(Feature feat);
		Task SendTelegramMessage(string fmt);
		Task<bool> TelegramSwitch(decimal speed, Celestial attacked = null, bool fromTelegram = false);
		Task SleepNow(DateTime WakeUpTime);
	}
}
