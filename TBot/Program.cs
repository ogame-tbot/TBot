using System;
using System.Collections;
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
		static List<TBotMain> instances = new();

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

			// Context validation
			//	a - Ogamed binary is present on same directory ?
			//	b - Settings file does exist ?
			if (File.Exists(Path.Combine(Path.GetFullPath(AppContext.BaseDirectory), OgamedService.GetExecutableName())) == false) {
				Helpers.WriteLog(LogType.Error, LogSender.Main, $"\"{OgamedService.GetExecutableName()}\" not found. Cannot proceed...");
				Environment.Exit(-1);
			}
			else if (File.Exists(settingPath) == false) {
				Helpers.WriteLog(LogType.Error, LogSender.Main, $"\"{settingPath}\" not found. Cannot proceed...");
				Environment.Exit(-1);
			}

			// Read settings first
			mainSettings = SettingsService.GetSettings(settingPath);

			// Initialize TelegramMessenger if enabled on main settings
			if ((bool) mainSettings.TelegramMessenger.Active) {
				Helpers.WriteLog(LogType.Info, LogSender.Main, "Activating Telegram Messenger");
				telegramMessenger = new TelegramMessenger((string) mainSettings.TelegramMessenger.API, (string) mainSettings.TelegramMessenger.ChatId);
				Thread.Sleep(1000);
				telegramMessenger.TelegramBot();
			}
			else
			{
				Helpers.WriteLog(LogType.Info, LogSender.Main, "Telegram Messenger disabled");
			}

			// Detect settings versioning by checking existence of "Instances" key
			SettingsVersion settingVersion = SettingsVersion.Invalid;

			if (SettingsService.IsSettingSet(mainSettings, "Instances") == false) {
				settingVersion = SettingsVersion.AllInOne;
				// Start only an instance of TBot
				StartTBotMain(settingPath, "MAIN");
			}
			else {
				// In this case we need a json formatted like follows:
				//	"Instances": [
				//		{
				//			"Settings": "<relative to main settings path>",
				//			"Alias": "<Custom name for this instance>"
				//		}
				// ]
				settingVersion = SettingsVersion.MultipleInstances;
				// Initialize all the instances of TBot found in main settings
				ICollection json_instances = mainSettings.Instances;
				Helpers.WriteLog(LogType.Info, LogSender.Main, $"Initializing {json_instances.Count} instances...");
				foreach (var instance in mainSettings.Instances) {
					if ( (SettingsService.IsSettingSet(instance, "Settings") == false) || (SettingsService.IsSettingSet(instance, "Alias") == false) ) {
						continue;
					}

					string settingsPath = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(settingPath), instance.Settings)).FullName;
					string alias = instance.Alias;

					StartTBotMain(settingsPath, alias);
				}
			}

			Helpers.WriteLog(LogType.Info, LogSender.Main, $"SettingsVersion Detected {settingVersion.ToString()} and managed. Press CTRL+C to exit");
			var exitEvt = new ManualResetEvent(false);
			Console.CancelKeyPress += (sender, e) => {
				Helpers.WriteLog(LogType.Info, LogSender.Main, "CTRL+C pressed!");
				e.Cancel = true;
				exitEvt.Set();
			};


			exitEvt.WaitOne();

			Helpers.WriteLog(LogType.Info, LogSender.Main, "Closing up...");
			foreach (var instance in instances) {
				instance.deinit();
			}
			Helpers.WriteLog(LogType.Info, LogSender.Main, "Goodbye!");
			instances.Clear();
		}

		private static void StartTBotMain(string settingsPath, string alias) {
			Helpers.WriteLog(LogType.Warning, LogSender.Main, $"Initializing instance \"{alias}\" \"{settingsPath}\"");
			try {
				if(File.Exists(settingsPath) == false) {
					Helpers.WriteLog(LogType.Warning, LogSender.Main, $"Instance \"{alias}\" cannot be initialized. \"{settingsPath}\" does not exist");
				}
				else {
					var tbot = new TBotMain(settingsPath, alias, telegramMessenger);
					if (tbot.init() == false) {
						Helpers.WriteLog(LogType.Warning, LogSender.Main, $"Error initializing instance \"{alias}\"");
					} else {
						Helpers.WriteLog(LogType.Info, LogSender.Main, $"Instance \"{alias}\" initialized.");
						instances.Add(tbot);
					}
				}
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Warning, LogSender.Main, $"Exception happened during initialization \"{e.Message}\"");
			}
		}
	}
}
