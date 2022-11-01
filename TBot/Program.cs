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
		static string logPath = Path.Combine(Directory.GetCurrentDirectory(), "log");

		static LoggerService.Logger loggerCtx = null;
		static TelegramMessenger telegramMessenger = null;
		static SemaphoreSlim instancesSem = new SemaphoreSlim(1, 1);
		static List<InstanceData> instances = new();
		static SettingsFileWatcher settingsWatcher = null;

		static ManualResetEvent exitEvt = null;

		static async Task Main(string[] args) {
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
				logPath = Path.GetFullPath(CmdLineArgsService.logPath.Get());
			}

			// Initialize Serilog here
			loggerCtx = new LoggerService.Logger(logPath);

			Helpers.LogToConsole(LogType.Info, LogSender.Tbot, $"Settings file	\"{settingPath}\"");
			Helpers.LogToConsole(LogType.Info, LogSender.Tbot, $"LogPath		\"{logPath}\"");

			// Context validation
			//	a - Ogamed binary is present on same directory ?
			//	b - Settings file does exist ?
			if (File.Exists(Path.Combine(Path.GetFullPath(AppContext.BaseDirectory), OgamedService.GetExecutableName())) == false) {
				LoggerService.Logger.WriteLog(LogType.Error, LogSender.Main, $"\"{OgamedService.GetExecutableName()}\" not found. Cannot proceed...");
				Environment.Exit(-1);
			}
			else if (File.Exists(settingPath) == false) {
				LoggerService.Logger.WriteLog(LogType.Error, LogSender.Main, $"\"{settingPath}\" not found. Cannot proceed...");
				Environment.Exit(-1);
			}

			// Load and handle settings
			await LoadSettings();

			exitEvt = new ManualResetEvent(false);
			Console.CancelKeyPress += (sender, e) => {
				LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, "CTRL+C pressed!");
				e.Cancel = true;
				exitEvt.Set();
			};


			exitEvt.WaitOne();

			LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, "Closing up...");
			List<Task> deinitTasks = new();
			foreach (var instance in instances) {
				LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, $"Deinitializing instance \"{instance._alias}\" \"{instance._botSettingsPath}\"");
				deinitTasks.Add(instance._botMain.deinit());
			}
			Task.WaitAll(deinitTasks.ToArray());
			LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, "Goodbye!");
			instances.Clear();
		}

		private static async Task<TBotMain> StartTBotMain(string settingsPath, string alias) {
			LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, $"Starting instance \"{alias}\" \"{settingsPath}\"");

			try {
				if(File.Exists(settingsPath) == false) {
					LoggerService.Logger.WriteLog(LogType.Warning, LogSender.Main, $"Instance \"{alias}\" cannot be initialized. \"{settingsPath}\" does not exist");
				}
				else {
					var tbot = new TBotMain(settingsPath, alias, telegramMessenger);
					var initialized = await tbot.init();

					if (initialized == false) {
						LoggerService.Logger.WriteLog(LogType.Warning, LogSender.Main, $"Error initializing instance \"{alias}\"");
						tbot = null;
					} else {
						LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, $"Instance \"{alias}\" initialized.");
						return tbot;
					}
				}
			} catch (Exception e) {
				LoggerService.Logger.WriteLog(LogType.Warning, LogSender.Main, $"Exception happened during initialization \"{e.Message}\"");
			}

			return null;
		}

		private static void InitializeTelegramMessenger() {
			if (
				SettingsService.IsSettingSet(mainSettings, "TelegramMessenger") &&
				SettingsService.IsSettingSet(mainSettings.TelegramMessenger, "Active") &&
				(bool) mainSettings.TelegramMessenger.Active
			) {
				if (telegramMessenger == null) {
					LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, "Activating Telegram Messenger");
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
					LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, "Telegram Messenger AutoPing is enabled!");
					long everyHours = (long) mainSettings.TelegramMessenger.TelegramAutoPing.EveryHours;
					if (everyHours == 0) {
						LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, "Telegram Messenger AutoPing EveryHours is 0. Setting to 1 hour.");
						everyHours = 1;
					}

					telegramMessenger.StartAutoPing(everyHours);
				} else {
					LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, "Telegram Messenger AutoPing disabled.");
				}
			} else {
				LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, "Telegram Messenger disabled");

				if (telegramMessenger != null) {
					// FIXME. If it was initialized and existing instances are using it, then it may crash.
					telegramMessenger.TelegramBotDisable();
					telegramMessenger = null;
				}
			}
		}

		private static async void OnSettingsChanged() {
			LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, "Core settings file changed.");

			await LoadSettings();
		}

		private static async Task LoadSettings() {
			await instancesSem.WaitAsync();

			LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, $"Reading settings \"{settingPath}\"");

			// Read settings first
			mainSettings = SettingsService.GetSettings(settingPath);

			// Initialize TelegramMessenger if enabled on main settings
			InitializeTelegramMessenger();

			// Detect settings versioning by checking existence of "Instances" key
			SettingsVersion settingVersion = SettingsVersion.Invalid;

			// We are going to gather valid Instances to be inited. The execution will be like this:
			//	Await deinit of any removed instances
			//	Async init of good instances
			List<InstanceData> newInstances = new();
			List<Task<TBotMain>> awaitingInstances = new();
			Dictionary<string, string> instancesToBeInited = new();	// Key -> Alias, Value -> settings

			if (SettingsService.IsSettingSet(mainSettings, "Instances") == false) {
				LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, "Single instance settings detected");
				settingVersion = SettingsVersion.AllInOne;

				// Start only an instance of TBot
				instancesToBeInited.Add("MAIN", settingPath);
				
			} else {
				// In this case we need a json formatted like follows:
				//	"Instances": [
				//		{
				//			"Settings": "<relative to main settings path>",
				//			"Alias": "<Custom name for this instance>"
				//		}
				// ]
				LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, "Multiples instances settings detected");
				settingVersion = SettingsVersion.MultipleInstances;
				
				// Initialize all the instances of TBot found in main settings
				ICollection json_instances = mainSettings.Instances;
				LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, $"Initializing {json_instances.Count} instances...");
				foreach (var instance in mainSettings.Instances) {
					if ((SettingsService.IsSettingSet(instance, "Settings") == false) || (SettingsService.IsSettingSet(instance, "Alias") == false)) {
						continue;
					}
					string cInstanceSettingPath = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(settingPath), instance.Settings)).FullName;
					string alias = instance.Alias;

					// Check if already initialized. if that so, update alias and keep going
					if (instances.Any(c => c._botSettingsPath == cInstanceSettingPath) == true) {
						LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, $"Instance \"{alias}\" \"{cInstanceSettingPath}\" already inited.");
						var foundInstance = instances.First(c => c._botSettingsPath == cInstanceSettingPath);
						foundInstance._alias = alias;

						newInstances.Add(foundInstance);
					}
					else {
						LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, $"Enqueueing initialization of instance \"{alias}\" \"{cInstanceSettingPath}\"");
						instancesToBeInited.Add(alias, cInstanceSettingPath);
					}
				}

				// Deinitialize instances that are no more valid
				foreach (var instance in instances) {
					if (newInstances.Any(c => c._botSettingsPath == instance._botSettingsPath) == false) {
						LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, $"Deinitializing instance \"{instance._alias}\" \"{instance._botSettingsPath}\"");

						await instance._botMain.deinit();
					}
				}

				// Now Async initialize "validated" instances
				foreach(var instanceToBeInited in instancesToBeInited) {
					string cInstanceSettingPath = instanceToBeInited.Value;
					string alias = instanceToBeInited.Key;
					LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, $"Asynchronously initializing instance \"{alias}\" \"{cInstanceSettingPath}\"");
					awaitingInstances.Add(StartTBotMain(cInstanceSettingPath, alias));
				}

				// Await initialization and add initialized instances
				foreach(Task<TBotMain> awaitingInstance in awaitingInstances) {
					TBotMain uniqueInstance = await awaitingInstance;
					if (uniqueInstance != null) {
						InstanceData instance = new InstanceData(uniqueInstance, uniqueInstance.settingsPath, uniqueInstance.instanceAlias);
						newInstances.Add(instance);
					}
				}

				// Finally, swap lists
				instances.Clear();
				instances = newInstances;

				// Initialize settingsWatcher 
				if (settingsWatcher == null)
					settingsWatcher = new SettingsFileWatcher(OnSettingsChanged, settingPath);
			}

			instancesSem.Release();

			LoggerService.Logger.WriteLog(LogType.Info, LogSender.Main, $"SettingsVersion Detected {settingVersion.ToString()} and managed. Press CTRL+C to exit");
		}
	}
}
