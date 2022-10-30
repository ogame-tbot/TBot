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

	sealed class InstanceData : IEquatable<InstanceData> {
		public TBotMain _botMain;
		public string _botSettingsPath;
		public string _alias;

		public InstanceData(TBotMain botMain, string botSettingsPath, string alias) {
			_botMain = botMain;
			_botSettingsPath = botSettingsPath;
			_alias = alias;
		}

		public bool Equals(InstanceData other) {
			// Equality must check of settings path is the same, since user may change Alias and settings internal data
			if (other == null)
				return false;

			return (_botSettingsPath == other._botSettingsPath);
		}
	}
	class Program {

		static DateTime startTime = DateTime.UtcNow;
		static string settingPath = Path.Combine(Path.GetFullPath(AppContext.BaseDirectory), "settings.json");
		static dynamic mainSettings;

		static TelegramMessenger telegramMessenger = null;
		static List<InstanceData> instances = new();
		static SettingsFileWatcher settingsWatcher = null;

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
			InitializeTelegramMessenger();

			// Detect settings versioning by checking existence of "Instances" key
			SettingsVersion settingVersion = SettingsVersion.Invalid;

			if (SettingsService.IsSettingSet(mainSettings, "Instances") == false) {
				settingVersion = SettingsVersion.AllInOne;
				// Start only an instance of TBot
				StartTBotMain(settingPath, "MAIN", ref instances);
			}
			else
			{
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

					StartTBotMain(settingsPath, alias, ref instances);
				}

				settingsWatcher = new SettingsFileWatcher(OnSettingsChanged, settingPath);
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
				Helpers.WriteLog(LogType.Info, LogSender.Main, $"Deinitializing instance \"{instance._alias}\" \"{instance._botSettingsPath}\"");
				instance._botMain.deinit();
			}
			Helpers.WriteLog(LogType.Info, LogSender.Main, "Goodbye!");
			instances.Clear();
		}

		private static void StartTBotMain(string settingsPath, string alias, ref List<InstanceData> instanceList) {
			Helpers.WriteLog(LogType.Warning, LogSender.Main, $"Initializing instance \"{alias}\" \"{settingsPath}\"");
			
			try {
				if(File.Exists(settingsPath) == false) {
					Helpers.WriteLog(LogType.Warning, LogSender.Main, $"Instance \"{alias}\" cannot be initialized. \"{settingsPath}\" does not exist");
				}
				else {
					var tbot = new TBotMain(settingsPath, alias, telegramMessenger);
					if (tbot.init().Result == false) {
						Helpers.WriteLog(LogType.Warning, LogSender.Main, $"Error initializing instance \"{alias}\"");
					} else {
						Helpers.WriteLog(LogType.Info, LogSender.Main, $"Instance \"{alias}\" initialized.");
						InstanceData instance = new InstanceData(tbot, settingsPath, alias);
						instanceList.Add(instance);
					}

				}
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Warning, LogSender.Main, $"Exception happened during initialization \"{e.Message}\"");
			}
		}

		private static void InitializeTelegramMessenger() {
			if (
				SettingsService.IsSettingSet(mainSettings, "TelegramMessenger") &&
				SettingsService.IsSettingSet(mainSettings.TelegramMessenger, "Active") &&
				(bool) mainSettings.TelegramMessenger.Active
			) {
				if (telegramMessenger == null) {
					Helpers.WriteLog(LogType.Info, LogSender.Main, "Activating Telegram Messenger");
					telegramMessenger = new TelegramMessenger((string) mainSettings.TelegramMessenger.API, (string) mainSettings.TelegramMessenger.ChatId);
					Thread.Sleep(1000);
					telegramMessenger.TelegramBot();
				}

				// Check autoping
				if (
					SettingsService.IsSettingSet(mainSettings.TelegramMessenger, "TelegramAutoPing") &&
					SettingsService.IsSettingSet(mainSettings.TelegramMessenger.TelegramAutoPing, "Active") &&
					SettingsService.IsSettingSet(mainSettings.TelegramMessenger.TelegramAutoPing, "EveryHours") &&
					(bool) mainSettings.TelegramMessenger.TelegramAutoPing.Active
				) {
					Helpers.WriteLog(LogType.Info, LogSender.Main, "Telegram Messenger AutoPing is enabled!");
					long everyHours = (long) mainSettings.TelegramMessenger.TelegramAutoPing.EveryHours;
					if (everyHours == 0) {
						Helpers.WriteLog(LogType.Info, LogSender.Main, "Telegram Messenger AutoPing EveryHours is 0. Setting to 1 hour.");
						everyHours = 1;
					}

					telegramMessenger.StartAutoPing(everyHours);
				} else {
					Helpers.WriteLog(LogType.Info, LogSender.Main, "Telegram Messenger AutoPing disabled.");
				}
			} else {
				Helpers.WriteLog(LogType.Info, LogSender.Main, "Telegram Messenger disabled");

				if (telegramMessenger != null) {
					telegramMessenger.TelegramBotDisable();
					telegramMessenger = null;
				}
			}
		}

		private static void OnSettingsChanged() {
			Helpers.WriteLog(LogType.Info, LogSender.Main, "Core settings file changed.");

			mainSettings = SettingsService.GetSettings(settingPath);

			// Handle Telegram section
			InitializeTelegramMessenger();

			// Remove / Add instances
			List<InstanceData> newInstances = new();
			ICollection json_instances = mainSettings.Instances;
			Helpers.WriteLog(LogType.Info, LogSender.Main, $"Detected {json_instances.Count} instances...");
			// Existing instances will be kept, new one will be created.
			foreach (var instance in mainSettings.Instances) {
				// Validate JSON first
				if ((SettingsService.IsSettingSet(instance, "Settings") == false) || (SettingsService.IsSettingSet(instance, "Alias") == false)) {
					Helpers.WriteLog(LogType.Info, LogSender.Main, "Wrong element in array found. \"Settings\" and \"Alias\" are not correctly set");
					continue;
				}

				string settingsPath = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(settingPath), instance.Settings)).FullName;
				string alias = instance.Alias;

				Helpers.WriteLog(LogType.Info, LogSender.Main, $"Handling instance \"{alias}\" \"{settingPath}\"");
				// Check if settings is already in our instances List
				if (instances.Any(c => c._botSettingsPath == settingsPath) == true) {
					Helpers.WriteLog(LogType.Info, LogSender.Main, $"Instance \"{settingPath}\" was already present! Doing nothing here.");
					// Already inside our list!
					var oldInstance = instances.First(c => c._botSettingsPath == settingsPath);
					oldInstance._alias = alias;
					newInstances.Add(oldInstance);

					// Remove from instances
					instances.Remove(oldInstance);
				} else {
					// Initialize new instance. StartTBotMain will take care of checking if file exists and enqueueing inside our Collection
					StartTBotMain(settingsPath, alias, ref newInstances);
				}
			}

			// Deinitialize instances that are no more valid
			foreach (var instance in instances) {
				if (newInstances.Any(c => c._botSettingsPath == instance._botSettingsPath) == false) {
					Helpers.WriteLog(LogType.Info, LogSender.Main, $"Deinitializing instance \"{instance._alias}\" \"{instance._botSettingsPath}\"");
					// This is a very long process. Initialize a timer to print periodically what we are doing...
					var tim = new Timer(o => {
						Helpers.WriteLog(LogType.Info, LogSender.Main, $"Waiting for deinitialization of instance \"{instance._alias}\" \"{instance._botSettingsPath}\"...");
					}, null, 3000, Timeout.Infinite);
					instance._botMain.deinit();
					tim.Dispose();
				}
			}

			// Finally, swap lists
			instances.Clear();
			instances = newInstances;
		}
	}
}
