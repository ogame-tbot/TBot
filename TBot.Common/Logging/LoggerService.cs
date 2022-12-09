using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks;
using TBot.Common.Logging;
using TBot.Common.Logging.Enrichers;
using TBot.Common.Logging.Hooks;
using TBot.Common.Logging.Sinks;
using TBot.Common.Logging.TextFormatters;
using System.Reflection;
using System.Runtime.InteropServices;
using Serilog.Context;
using Serilog.Core;
using Serilog.Filters;
using Microsoft.AspNetCore.SignalR;
using TBot.Common.Logging.Hubs;
using Serilog.Sinks.AspNetCore.SignalR.Extensions;
using System.Globalization;
using Microsoft.AspNetCore.Routing.Template;

namespace TBot.Common.Logging {
	public class LoggerService<T> : ILoggerService<T> {

		private readonly IHubContext<WebHub, IWebHub> _hub;
		private readonly IServiceProvider _serviceProvider;

		public LoggerService(IHubContext<WebHub, IWebHub> hub,
			IServiceProvider serviceProvider) {
			_hub = hub;
			_serviceProvider = serviceProvider;
		}

		private object syncObject = new object();
		private static string _logPath = "";

		private LoggingLevelSwitch _telegramLevelSwitch = new LoggingLevelSwitch(LogEventLevel.Verbose);
		private bool _telegramAdded = false;

		public void WriteLog(LogLevel level, LogSender sender, string message) {
			IDisposable? telegram = null;

			if (_telegramAdded == true) {
				telegram = LogContext.PushProperty("TelegramEnabled", true);
			}
			using (LogContext.PushProperty("LogSender", sender))
			using (LogContext.PushProperty("LogLevel", level))
			using (LogContext.PushProperty("LogSenderEmoji", EmojiFormatter.GetEmoji(sender.ToString())))
			using (LogContext.PushProperty("LogLevelEmoji", EmojiFormatter.GetEmoji(level.ToString())))
			{
				switch (level) {
					case LogLevel.Trace:
						Log.Logger.Verbose(message);
						break;
					case LogLevel.Debug:
						Log.Logger.Debug(message);
						break;
					case LogLevel.Information:
						Log.Logger.Information(message);
						break;
					case LogLevel.Warning:
						Log.Logger.Warning(message);
						break;
					case LogLevel.Error:
						Log.Logger.Error(message);
						break;
					case LogLevel.Critical:
						Log.Logger.Fatal(message);
						break;
				}
			}

			if (telegram != null) {
				telegram.Dispose();
			}
		}

		private LoggerConfiguration GetDefaultConfiguration() {
			string outTemplate = "[{Timestamp:HH:mm:ss.fff zzz} {ThreadId} {Level:u3} {LogSender}] {Message:lj}{NewLine}{Exception}";
			long maxFileSize = 1 * 1024 * 1024 * 10;

			var logConfig = new LoggerConfiguration()
				.Enrich.With(new ThreadIdEnricher())
				.Enrich.FromLogContext()
			// Console
				.WriteTo.TBotColoredConsole(
					outputTemplate: outTemplate
				)
				// Log file
				.WriteTo.File(
					path: Path.Combine(_logPath, "TBot.log"),
					buffered: false,
					flushToDiskInterval: TimeSpan.FromHours(1),
					rollOnFileSizeLimit: true,
					fileSizeLimitBytes: maxFileSize,
					retainedFileCountLimit: 10,
					rollingInterval: RollingInterval.Day)
				// CSV
				.WriteTo.File(
					path: Path.Combine(_logPath, "TBot.csv"),
					buffered: false,
					hooks: new SerilogCSVHeaderHooks(),
					formatter: new SerilogCSVTextFormatter(),
					flushToDiskInterval: TimeSpan.FromHours(1),
					rollOnFileSizeLimit: true,
					fileSizeLimitBytes: maxFileSize,
					rollingInterval: RollingInterval.Day)
				.WriteTo.SignalRTBotSink<WebHub, IWebHub>(
					LogEventLevel.Verbose,
					_serviceProvider,
					CultureInfo.InvariantCulture, // can be null
					new string[] { },        // can be null
					new string[] { },        // can be null
					new string[] { },        // can be null
					false) // false is the default value
				.MinimumLevel.Verbose();

			return logConfig;
		}

		public void ConfigureLogging(string logPath) {
			lock (syncObject) {
				_logPath = logPath;

				var logConfig = GetDefaultConfiguration();

				// Telegram default values
				_telegramLevelSwitch.MinimumLevel = LogEventLevel.Verbose;
				_telegramAdded = false;

				Log.Logger = logConfig.CreateLogger();

			}
		}

		public void AddTelegramLogger(string botToken, string chatId) {
			lock (syncObject) {
				if (_telegramAdded == false) {
					var logConfig = GetDefaultConfiguration();
					Log.Logger = logConfig.WriteTo.Logger(
							c => c.Filter.Equals(Matching.WithProperty<bool>("TelegramEnabled", p => p == true))
							).WriteTo.Telegram(botToken: botToken,
								chatId: chatId,
								dateFormat: null,
								outputTemplate: "{LogLevelEmoji:l}{LogSenderEmoji:l} {Message:lj}{NewLine}{Exception}")
						.CreateLogger();

					_telegramAdded = true;
				}
			}
		}

		public void RemoveTelegramLogger() {
			ConfigureLogging(_logPath);
		}

		public bool IsTelegramLoggerEnabled() {
			return _telegramAdded;
		}

		public void SetTelegramLoggerLogLevel(LogEventLevel logLevel) {
			lock (syncObject) {
				if (logLevel != _telegramLevelSwitch.MinimumLevel) {
					WriteLog(LogLevel.Warning, LogSender.Main, $"Telegram log level changed from {_telegramLevelSwitch.MinimumLevel.ToString()}" +
						$" into {logLevel.ToString()}");
				}
				_telegramLevelSwitch.MinimumLevel = logLevel;
			}
		}

		public LogEventLevel GetTelegramLoggerLevel() {
			return _telegramLevelSwitch.MinimumLevel;
		}
	}
}
