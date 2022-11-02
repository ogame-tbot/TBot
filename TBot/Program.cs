using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Serilog;
using Tbot.Exceptions;
using Tbot.Helpers;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Common.Logging.Enrichers;
using TBot.Common.Logging.Hooks;
using TBot.Common.Logging.Sinks;
using TBot.Common.Logging.TextFormatters;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;

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
		private static ILoggerService<Program> _logger;
		private static IServiceProvider _serviceProvider;
		static DateTime startTime = DateTime.UtcNow;
		static string settingPath = Path.Combine(Path.GetFullPath(AppContext.BaseDirectory), "settings.json");
		static dynamic mainSettings;

		static ITelegramMessenger telegramMessenger = null;
		static List<InstanceData> instances = new();
		static SettingsFileWatcher settingsWatcher = null;

		static void Main(string[] args) {
			MainAsync(args).Wait();
		}
		private static void ConfigureSerilog(string logPath, bool telegramLogging) {
			string outTemplate = "[{Timestamp:HH:mm:ss.fff zzz} {ThreadId} {Level:u3} {LogSender}] {Message:lj}{NewLine}{Exception}";
			long maxFileSize = 1 * 1024 * 1024 * 10;
			var loggerConfiguration = new LoggerConfiguration()
			.MinimumLevel.Debug()
			.Enrich.With(new ThreadIdEnricher())
			// Console
			.WriteTo.TBotColoredConsole(
				outputTemplate: outTemplate
			)
			// Log file
			.WriteTo.File(
				path: Path.Combine(logPath, "TBot.log"),
				buffered: false,
				flushToDiskInterval: TimeSpan.FromHours(1),
				rollOnFileSizeLimit: true,
				fileSizeLimitBytes: maxFileSize,
				retainedFileCountLimit: 10,
				rollingInterval: RollingInterval.Day)
			// CSV
			.WriteTo.File(
				path: Path.Combine(logPath, "TBot.csv"),
				buffered: false,
				hooks: new SerilogCSVHeaderHooks(),
				formatter: new SerilogCSVTextFormatter(),
				flushToDiskInterval: TimeSpan.FromHours(1),
				rollOnFileSizeLimit: true,
				fileSizeLimitBytes: maxFileSize,
				rollingInterval: RollingInterval.Day);

			if (telegramLogging) {
				loggerConfiguration.WriteTo.Telegram(botToken: (string) mainSettings.TelegramMessenger.API,
					chatId: (string) mainSettings.TelegramMessenger.ChatId,
					dateFormat: null,
					outputTemplate: "{LogLevelEmoji:l} {LogSenderEmoji:l}: {Message:lj}{NewLine}{Exception}");
			}
			

			Log.Logger = loggerConfiguration.CreateLogger();
		}
		static async Task MainAsync(string[] args) {

			//setup our DI
			_serviceProvider = new ServiceCollection()
				.AddSingleton(typeof(ILoggerService<>), typeof(LoggerService<>))
				.AddScoped<IHelpersService, HelpersService>()
				.AddScoped<IOgameService, OgameService>()
				.BuildServiceProvider();

			_logger = _serviceProvider.GetRequiredService<ILoggerService<Program>>();
			var ogameService = _serviceProvider.GetRequiredService<IOgameService>();
			var helpersService = _serviceProvider.GetRequiredService<IHelpersService>();

			helpersService.SetTitle();

			CmdLineArgsService.DoParse(args);
			if (CmdLineArgsService.printHelp) {
				ColoredConsoleWriter.LogToConsole(LogLevel.Information, LogSender.Tbot, $"{System.AppDomain.CurrentDomain.FriendlyName} {CmdLineArgsService.helpStr}");
				Environment.Exit(0);
			}

			if (CmdLineArgsService.settingsPath.IsPresent) {
				settingPath = Path.GetFullPath(CmdLineArgsService.settingsPath.Get());
			}

			var logPath = Path.Combine(Directory.GetCurrentDirectory(), "log");
			if (CmdLineArgsService.logPath.IsPresent) {
				logPath = Path.GetFullPath(CmdLineArgsService.logPath.Get());
			}

			// Read settings first
			mainSettings = SettingsService.GetSettings(settingPath);

			var telegramLogging = SettingsService.IsSettingSet(mainSettings, "TelegramMessenger") &&
				SettingsService.IsSettingSet(mainSettings.TelegramMessenger, "Active") &&
				SettingsService.IsSettingSet(mainSettings.TelegramMessenger, "Logging") &&
				(bool) mainSettings.TelegramMessenger.Active &&
				(bool) mainSettings.TelegramMessenger.Logging;

			ConfigureSerilog(logPath, telegramLogging);

			_logger.WriteLog(LogLevel.Information, LogSender.Tbot, $"Settings file	\"{settingPath}\"");
			//_logger.Log(LogLevel.Information, LogSender.Tbot, $"LogPath		\"{Helpers.logPath}\"");

			// Context validation
			//	a - Ogamed binary is present on same directory ?
			//	b - Settings file does exist ?
			if (!ogameService.ValidatePrerequisites()) {
				Environment.Exit(-1);
			} else if (File.Exists(settingPath) == false) {
				_logger.WriteLog(LogLevel.Error, LogSender.Main, $"\"{settingPath}\" not found. Cannot proceed...");
				Environment.Exit(-1);
			}

			// Initialize TelegramMessenger if enabled on main settings
			InitializeTelegramMessenger();

			// Detect settings versioning by checking existence of "Instances" key
			SettingsVersion settingVersion = SettingsVersion.Invalid;

			if (SettingsService.IsSettingSet(mainSettings, "Instances") == false) {
				settingVersion = SettingsVersion.AllInOne;
				// Start only an instance of TBot
				var initializedInstance = await StartTBotMain(settingPath, "MAIN");
				instances.Add(initializedInstance);
			} else {
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
				_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Initializing {json_instances.Count} instances...");
				foreach (var instance in mainSettings.Instances) {
					if ((SettingsService.IsSettingSet(instance, "Settings") == false) || (SettingsService.IsSettingSet(instance, "Alias") == false)) {
						continue;
					}
					string settingsPath = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(settingPath), instance.Settings)).FullName;
					string alias = instance.Alias;

					var initializedInstance = await StartTBotMain(settingsPath, alias);
					instances.Add(initializedInstance);
				}

				settingsWatcher = new SettingsFileWatcher(OnSettingsChanged, settingPath);
			}

			_logger.WriteLog(LogLevel.Information, LogSender.Main, $"SettingsVersion Detected {settingVersion.ToString()} and managed. Press CTRL+C to exit");

			var tcs = new TaskCompletionSource();

			Console.CancelKeyPress += (sender, e) => {
				_logger.WriteLog(LogLevel.Information, LogSender.Main, "CTRL+C pressed!");
				tcs.SetResult();
			};

			await tcs.Task;

			_logger.WriteLog(LogLevel.Information, LogSender.Main, "Closing up...");
			foreach (var instance in instances) {
				_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Deinitializing instance \"{instance._alias}\" \"{instance._botSettingsPath}\"");
				await instance._botMain.deinit();
			}
			_logger.WriteLog(LogLevel.Information, LogSender.Main, "Goodbye!");
			instances.Clear();
		}


		private static async Task<InstanceData> StartTBotMain(string settingsPath, string alias) {
			_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Initializing instance \"{alias}\" \"{settingsPath}\"");

			try {
				if (File.Exists(settingsPath) == false) {
					_logger.WriteLog(LogLevel.Warning, LogSender.Main, $"Instance \"{alias}\" cannot be initialized. \"{settingsPath}\" does not exist");
					throw new MissingConfigurationException($"Instance \"{alias}\" cannot be initialized. \"{settingsPath}\" does not exist");
				} else {
					using var scope = _serviceProvider.CreateScope();
					var ogamedService = scope.ServiceProvider.GetRequiredService<IOgameService>();
					var tbotLogger = scope.ServiceProvider.GetRequiredService<ILoggerService<TBotMain>>();
					var helpersService = scope.ServiceProvider.GetRequiredService<IHelpersService>();
					var tbot = new TBotMain(ogamedService, helpersService, tbotLogger);
					await tbot.Init(settingsPath, alias, telegramMessenger);
					_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Instance \"{alias}\" initialized successfully!");
					InstanceData instance = new InstanceData(tbot, settingsPath, alias);
					return instance;
				}
			} catch (Exception e) {
				_logger.WriteLog(LogLevel.Error, LogSender.Main, $"Error initializing instance \"{{alias}}\": {e.Message}");
				throw;
			}
		}

		private static void InitializeTelegramMessenger() {
			if (
				SettingsService.IsSettingSet(mainSettings, "TelegramMessenger") &&
				SettingsService.IsSettingSet(mainSettings.TelegramMessenger, "Active") &&
				(bool) mainSettings.TelegramMessenger.Active
			) {
				if (telegramMessenger == null) {
					var telegramLogger = _serviceProvider.GetRequiredService<ILoggerService<TelegramMessenger>>();
					var helpersService = _serviceProvider.GetRequiredService<IHelpersService>();
					_logger.WriteLog(LogLevel.Information, LogSender.Main, "Activating Telegram Messenger");
					telegramMessenger = new TelegramMessenger(telegramLogger, helpersService, (string) mainSettings.TelegramMessenger.API, (string) mainSettings.TelegramMessenger.ChatId);
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
					_logger.WriteLog(LogLevel.Information, LogSender.Main, "Telegram Messenger AutoPing is enabled!");
					long everyHours = (long) mainSettings.TelegramMessenger.TelegramAutoPing.EveryHours;
					if (everyHours == 0) {
						_logger.WriteLog(LogLevel.Information, LogSender.Main, "Telegram Messenger AutoPing EveryHours is 0. Setting to 1 hour.");
						everyHours = 1;
					}

					telegramMessenger.StartAutoPing(everyHours);
				} else {
					_logger.WriteLog(LogLevel.Information, LogSender.Main, "Telegram Messenger AutoPing disabled.");
				}
			} else {
				_logger.WriteLog(LogLevel.Information, LogSender.Main, "Telegram Messenger disabled");

				if (telegramMessenger != null) {
					telegramMessenger.TelegramBotDisable();
					telegramMessenger = null;
				}
			}
		}

		private static async void OnSettingsChanged() {
			_logger.WriteLog(LogLevel.Information, LogSender.Main, "Core settings file changed.");

			mainSettings = SettingsService.GetSettings(settingPath);

			// Handle Telegram section
			InitializeTelegramMessenger();

			// Remove / Add instances
			List<InstanceData> newInstances = new();
			ICollection json_instances = mainSettings.Instances;
			_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Detected {json_instances.Count} instances...");
			// Existing instances will be kept, new one will be created.
			foreach (var instance in mainSettings.Instances) {
				// Validate JSON first
				if ((SettingsService.IsSettingSet(instance, "Settings") == false) || (SettingsService.IsSettingSet(instance, "Alias") == false)) {
					_logger.WriteLog(LogLevel.Information, LogSender.Main, "Wrong element in array found. \"Settings\" and \"Alias\" are not correctly set");
					continue;
				}

				string settingsPath = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(settingPath), instance.Settings)).FullName;
				string alias = instance.Alias;

				_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Handling instance \"{alias}\" \"{settingPath}\"");
				// Check if settings is already in our instances List
				if (instances.Any(c => c._botSettingsPath == settingsPath) == true) {
					_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Instance \"{settingPath}\" was already present! Doing nothing here.");
					// Already inside our list!
					var oldInstance = instances.First(c => c._botSettingsPath == settingsPath);
					oldInstance._alias = alias;
					newInstances.Add(oldInstance);

					// Remove from instances
					instances.Remove(oldInstance);
				} else {
					// Initialize new instance. StartTBotMain will take care of checking if file exists and enqueueing inside our Collection
					var initializedInstance = await StartTBotMain(settingsPath, alias);
					newInstances.Add(initializedInstance);
				}
			}

			// Deinitialize instances that are no more valid
			foreach (var instance in instances) {
				if (newInstances.Any(c => c._botSettingsPath == instance._botSettingsPath) == false) {
					_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Deinitializing instance \"{instance._alias}\" \"{instance._botSettingsPath}\"");
					// This is a very long process. Initialize a timer to print periodically what we are doing...
					var tim = new Timer(o => {
						_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Waiting for deinitialization of instance \"{instance._alias}\" \"{instance._botSettingsPath}\"...");
					}, null, 3000, Timeout.Infinite);
					await instance._botMain.deinit();
					tim.Dispose();
				}
			}

			// Finally, swap lists
			instances.Clear();
			instances = newInstances;
		}
	}
}
