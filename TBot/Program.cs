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
using Tbot.Workers;
using TBot.Common.Logging;
using TBot.Common.Logging.Enrichers;
using TBot.Common.Logging.Hooks;
using TBot.Common.Logging.Sinks;
using TBot.Common.Logging.TextFormatters;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;

namespace Tbot {

	class Program {
		private static ILoggerService<Program> _logger;
		private static IInstanceManager _instanceManager;
		static DateTime startTime = DateTime.UtcNow;

		static void Main(string[] args) {
			MainAsync(args).Wait();
		}
		static async Task MainAsync(string[] args) {

			var serviceProvider = new ServiceCollection()
				.AddSingleton(typeof(ILoggerService<>), typeof(LoggerService<>))
				.AddScoped<ICalculationService, CalculationService>()
				.AddScoped<IOgameService, OgameService>()
				.AddScoped<ITBotMain, TBotMain>()
				.AddScoped<ITBotOgamedBridge, TBotOgamedBridge>()
				.AddScoped<IFleetScheduler, FleetScheduler>()
				.AddScoped<IWorkerFactory, WorkerFactory>()
				.AddScoped<ITelegramMessenger, TelegramMessenger>()
				.AddScoped<IInstanceManager, InstanceManager>()
				.BuildServiceProvider();

			_logger = serviceProvider.GetRequiredService<ILoggerService<Program>>();
			_instanceManager = serviceProvider.GetRequiredService<IInstanceManager>();
			var ogameService = serviceProvider.GetRequiredService<IOgameService>();

			ConsoleHelpers.SetTitle();

			CmdLineArgsService.DoParse(args);
			if (CmdLineArgsService.printHelp) {
				ColoredConsoleWriter.LogToConsole(LogLevel.Information, LogSender.Tbot, $"{System.AppDomain.CurrentDomain.FriendlyName} {CmdLineArgsService.helpStr}");
				Environment.Exit(0);
			}

			if (CmdLineArgsService.settingsPath.IsPresent) {
				_instanceManager.SettingsAbsoluteFilepath = Path.GetFullPath(CmdLineArgsService.settingsPath.Get());
			}

			var logPath = Path.Combine(Directory.GetCurrentDirectory(), "log");
			if (CmdLineArgsService.logPath.IsPresent == true) {
				logPath = Path.GetFullPath(CmdLineArgsService.logPath.Get());
			}

			_logger.ConfigureLogging(logPath);

			// Context validation
			//	a - Ogamed binary is present on same directory ?
			//	b - Settings file does exist ?
			if (!ogameService.ValidatePrerequisites()) {
				Environment.Exit(-1);
			} else if (File.Exists(_instanceManager.SettingsAbsoluteFilepath) == false) {
				_logger.WriteLog(LogLevel.Error, LogSender.Main, $"\"{_instanceManager.SettingsAbsoluteFilepath}\" not found. Cannot proceed...");
				Environment.Exit(-1);
			}

			// Manage settings
			_instanceManager.OnSettingsChanged();

			// Wait for CTRL + C event
			var tcs = new TaskCompletionSource();

			Console.CancelKeyPress += (sender, e) => {
				_logger.WriteLog(LogLevel.Information, LogSender.Main, "CTRL+C pressed!");
				tcs.SetResult();
			};

			await tcs.Task;

			await _instanceManager.DisposeAsync();
			_logger.WriteLog(LogLevel.Information, LogSender.Main, "Goodbye!");
		}
	}
}
