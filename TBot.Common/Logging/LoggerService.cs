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
		private string _logPath = "";

		private LoggingLevelSwitch _telegramLevelSwitch = new LoggingLevelSwitch();
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
						Log.Verbose(message);
						break;
					case LogLevel.Debug:
						Log.Debug(message);
						break;
					case LogLevel.Information:
						Log.Information(message);
						break;
					case LogLevel.Warning:
						Log.Warning(message);
						break;
					case LogLevel.Error:
						Log.Error(message);
						break;
					case LogLevel.Critical:
						Log.Fatal(message);
						break;
				}
			}

			if (telegram != null) {
				telegram.Dispose();
			}
		}

		public void ConfigureLogging(string logPath) {
			lock (syncObject) {
				_logPath = logPath;
				string outTemplate = "[{Timestamp:HH:mm:ss.fff zzz} {ThreadId} {Level:u3} {LogSender}] {Message:lj}{NewLine}{Exception}";
				long maxFileSize = 1 * 1024 * 1024 * 10;
				var logConfig = new LoggerConfiguration()
				.MinimumLevel.Debug()
				.Enrich.With(new ThreadIdEnricher())
				.Enrich.FromLogContext()
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
					rollingInterval: RollingInterval.Day)
				.WriteTo.SignalRTBotSink<WebHub, IWebHub>(
					LogEventLevel.Debug,
					_serviceProvider,
					null, // can be null
					new string[] { },        // can be null
					new string[] { },        // can be null
					new string[] { },        // can be null
					false);          // false is the default value

				// Telegram default values
				_telegramLevelSwitch.MinimumLevel = LogEventLevel.Debug;
				_telegramAdded = false;

				Log.Logger = logConfig.CreateLogger();
			}
		}

		public void AddTelegramLogger(string botToken, string chatId) {
			lock (syncObject) {
				if (_telegramAdded == false) {
					Log.Logger = new LoggerConfiguration()
						.WriteTo.Logger(Log.Logger)
						// Control telegram level
						.MinimumLevel.ControlledBy(_telegramLevelSwitch)
						// Start anew with Enrichers
						.Enrich.With(new ThreadIdEnricher())
						.Enrich.FromLogContext()
						.WriteTo.Logger(
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
