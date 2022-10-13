using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json.Linq;
using Tbot.Includes;
using Tbot.Model;
using Tbot.Services;

namespace Tbot {
	class Program {

		static DateTime startTime = DateTime.UtcNow;
		static string settingPath = Path.Combine(Path.GetFullPath(AppContext.BaseDirectory), "settings.json");
		static dynamic mainSettings;

		static TelegramMessenger telegramMessenger;

		static void Main(string[] args) {
			Helpers.SetTitle();

			CmdLineArgsService.DoParse(args);
			if (CmdLineArgsService.printHelp) {
				Helpers.LogToConsole(LogType.Info, LogSender.Tbot, $"{System.AppDomain.CurrentDomain.FriendlyName} {CmdLineArgsService.helpStr}");
				Environment.Exit(0);
			}

			if (CmdLineArgsService.settingsPath.IsPresent) {
				settingPath = Path.GetFullPath(CmdLineArgsService.settingsPath.Get());
			}

			if (CmdLineArgsService.logPath.IsPresent) {
				Helpers.logPath = Path.GetFullPath(CmdLineArgsService.logPath.Get());
			}

			Helpers.LogToConsole(LogType.Info, LogSender.Tbot, $"Settings file	\"{settingPath}\"");
			Helpers.LogToConsole(LogType.Info, LogSender.Tbot, $"LogPath		\"{Helpers.logPath}\"");

			// Read settings first
			mainSettings = SettingsService.GetSettings(settingPath);

			// Initialize TelegramMessenger if enabled on main settings
			if ((bool) mainSettings.TelegramMessenger.Active) {
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Activating Telegram Messenger");
				telegramMessenger = new TelegramMessenger((string) mainSettings.TelegramMessenger.API, (string) mainSettings.TelegramMessenger.ChatId);
				Thread.Sleep(1000);
				telegramMessenger.TelegramBot();
			}

			// Initialize all the instances of TBot found in main settings
			var tbot = new TBotMain(settingPath, telegramMessenger);
			tbot.init();

			Helpers.SetTitle($"Managing accounts");

			Console.ReadLine();
		}
	}
}
